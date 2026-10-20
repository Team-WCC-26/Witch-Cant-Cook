using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public sealed partial class TEST_ECSComparison
{
    [Header("Interval CSV scenario (periodic spawn + timed pool return)")]
    [SerializeField, Min(.01f)] private float intervalSpawnSeconds = 2f;
    [SerializeField, Min(1)] private int intervalSpawnCount = 5;
    [SerializeField, Min(.01f)] private float intervalLifetimeSeconds = 10f;
    [SerializeField, Min(.1f)] private float intervalDurationSeconds = 60f;
    [SerializeField, Min(0)] private int intervalPrewarmCount = 50;
    [SerializeField, Min(.1f)] private float intervalBaselineSeconds = 2f;
    [SerializeField, Min(.1f)] private float intervalRecoverySeconds = 2f;
    [SerializeField, Min(1024)] private int intervalFrameCapacity = 262144;

    private bool csvRunInterval, csvIntervalBuffersPrepared;
    private double csvIntervalPeriod, csvIntervalLifetime, csvIntervalDuration, csvIntervalBaseline, csvIntervalRecovery;
    private double csvIntervalStartedAt;
    private int csvIntervalCount, csvIntervalPrewarm, csvIntervalPlannedBursts, csvIntervalCapacity, csvIntervalNextBurst;
    public bool IsIntervalScenario => IsCsvRunning ? csvRunInterval : csvScenario == CsvScenario.IntervalLifecycle;
    public int IntervalCompletedBursts => csvIntervalNextBurst;
    public int IntervalPlannedBursts => csvIntervalPlannedBursts;
    public int IntervalLiveCount => csvSpawner != null ? csvSpawner.IntervalActiveCount : 0;
    public int IntervalPending => csvSpawner != null ? csvSpawner.IntervalPendingCount : 0;
    public int IntervalSubmitted => csvSpawner != null ? csvSpawner.IntervalSubmittedTotal : 0;
    public int IntervalReturned => csvSpawner != null ? csvSpawner.IntervalReturnedTotal : 0;
    public int IntervalReused => csvSpawner != null ? csvSpawner.IntervalReusedTotal : 0;
    public double IntervalElapsedSeconds => IsCsvRunning && csvIntervalStartedAt > 0 ? Math.Max(0, Time.realtimeSinceStartupAsDouble - csvIntervalStartedAt) : 0;
    public double EstimatedIntervalCsvSeconds => Math.Max(600, csvRepetitions *
        ((double)intervalDurationSeconds + intervalLifetimeSeconds + intervalBaselineSeconds + intervalRecoverySeconds + csvSettleSeconds + 30) + 120);

    private void PrepareIntervalConfiguration()
    {
        foreach (float v in new[] { intervalSpawnSeconds, intervalLifetimeSeconds, intervalDurationSeconds, intervalBaselineSeconds, intervalRecoverySeconds })
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0) throw new InvalidOperationException("Interval seconds must be finite and positive.");
        if (intervalSpawnCount < 1 || intervalSpawnCount > 500 || intervalPrewarmCount < 0 || intervalPrewarmCount > 10000)
            throw new InvalidOperationException("Interval count must be 1..500; Prewarm count must be 0..10,000.");
        csvIntervalPeriod = intervalSpawnSeconds; csvIntervalLifetime = intervalLifetimeSeconds;
        csvIntervalDuration = intervalDurationSeconds; csvIntervalBaseline = intervalBaselineSeconds; csvIntervalRecovery = intervalRecoverySeconds;
        csvIntervalCount = intervalSpawnCount; csvIntervalPrewarm = intervalPrewarmCount;
        double ratio = csvIntervalDuration / csvIntervalPeriod;
        double nearest = Math.Round(ratio);
        // Inspector floats such as 0.6 / 0.05 must not accidentally schedule a 13th burst.
        if (nearest > 0 && Math.Abs(ratio - nearest) <= Math.Max(1, ratio) * 1e-7) ratio = nearest;
        double bursts = Math.Ceiling(ratio);
        if (bursts > 1000000 || bursts * csvIntervalCount * csvRepetitions > 1000000)
            throw new InvalidOperationException("Interval event capacity exceeds 1,000,000 ingredients. Reduce duration/count/repetitions.");
        csvIntervalPlannedBursts = (int)bursts;
        double live = Math.Min(bursts, Math.Ceiling(csvIntervalLifetime / csvIntervalPeriod) + 2) * csvIntervalCount;
        if (live > 10000) throw new InvalidOperationException("Interval live tracking exceeds 10,000 ingredients. Reduce lifetime/count or increase the interval.");
        csvIntervalCapacity = (int)live;
        csvIntervalBuffersPrepared = false;
        csvIntervalStartedAt = 0;
        csvIntervalNextBurst = 0;
    }

    private void AdvanceIntervalCsv(double now)
    {
        if (csvStage == CsvStage.Preparing)
        {
            if (!csvSpawner.CanSpawn || !csvSpawner.IntervalCleanupComplete) return;
            if (!csvIntervalBuffersPrepared)
            {
                csvSpawner.PrepareIntervalBuffers(csvIntervalCapacity,
                    checked(csvIntervalPlannedBursts * csvIntervalCount * csvRunRepetitions), csvIntervalPrewarm);
                csvIntervalBuffersPrepared = true;
            }
            csvSpawner.ResetIntervalCounters();
            csvIntervalStartedAt = 0; csvIntervalNextBurst = 0;
            csvSpawner.EmptyIngredientPool();
            if (csvSpawner.MemoryStage == TEST_PerformanceSpawner.MemoryTestStage.Failed) throw new InvalidOperationException(csvSpawner.Status);
            CreateCsvRecorders();
            SetCsvStage(CsvStage.Prewarm);
            if (!csvSpawner.PrewarmPool(csvIntervalPrewarm)) throw new InvalidOperationException(csvSpawner.Status);
        }
        else if (csvStage == CsvStage.Prewarm) FinishCsvOperation(now, CsvStage.PrewarmSettling);
        else if (csvStage == CsvStage.PrewarmSettling && now >= csvNextStageTime)
        {
            SetCsvStage(CsvStage.Baseline);
            csvNextStageTime = now + csvIntervalBaseline;
        }
        else if (csvStage == CsvStage.Baseline && now >= csvNextStageTime)
        {
            csvSpawner.StartIntervalRepetition(csvIntervalLifetime);
            csvIntervalStartedAt = now; csvIntervalNextBurst = 0;
            SetCsvStage(CsvStage.IntervalRunning);
            csvDeadline = now + csvIntervalDuration + 60;
            SubmitScheduledIntervalBurst(now);
        }
        else if (csvStage == CsvStage.IntervalRunning)
        {
            csvSpawner.ReturnDueIntervalItems(now);
            SubmitScheduledIntervalBurst(now);
            if (now >= csvIntervalStartedAt + csvIntervalDuration && csvIntervalNextBurst == csvIntervalPlannedBursts)
            {
                SetCsvStage(CsvStage.IntervalDraining);
                csvDeadline = now + csvIntervalLifetime + 60;
            }
        }
        else if (csvStage == CsvStage.IntervalDraining)
        {
            csvSpawner.ReturnDueIntervalItems(now);
            if (csvSpawner.IntervalActiveCount == 0 && csvSpawner.IntervalPendingCount == 0 && csvSpawner.IntervalCleanupComplete)
            {
                int expected = csvIntervalPlannedBursts * csvIntervalCount;
                if (csvSpawner.IntervalActivatedTotal != expected || csvSpawner.IntervalReturnedTotal != expected || csvSpawner.IntervalEntityCount != 0)
                    throw new InvalidOperationException("Interval activation/return/Entity count mismatch.");
                csvSpawner.StopIntervalTracking(false);
                SetCsvStage(CsvStage.IntervalRecovered);
                csvNextStageTime = now + csvIntervalRecovery;
            }
        }
        else if (csvStage == CsvStage.IntervalRecovered && now >= csvNextStageTime)
        {
            csvCase++;
            if (csvCase == csvRunRepetitions) { EndCsvCapture(CsvState.Completed, "Completed"); return; }
            SetCsvStage(CsvStage.Preparing);
        }
    }

    private void SubmitScheduledIntervalBurst(double now)
    {
        if (csvIntervalNextBurst >= csvIntervalPlannedBursts) return;
        double scheduled = csvIntervalStartedAt + csvIntervalNextBurst * csvIntervalPeriod;
        if (now < scheduled) return;
        // Fixed schedule; at most one burst per frame. Missed deadlines remain visible in events.csv.
        // Do not silently drop requests or combine several missed ticks into one giant spike.
        csvSpawner.SubmitIntervalBurst(csvIntervalCount, csvIntervalNextBurst + 1, csvCase + 1, scheduled, now);
        csvIntervalNextBurst++;
    }

    private void WriteIntervalSettings(StreamWriter writer)
    {
        WriteCsvSetting(writer, "interval_spawn_seconds", csvIntervalPeriod.ToString("R", CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_spawn_count", csvIntervalCount.ToString(CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_lifetime_seconds", csvIntervalLifetime.ToString("R", CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_duration_seconds", csvIntervalDuration.ToString("R", CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_prewarm_count", csvIntervalPrewarm.ToString(CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_baseline_seconds", csvIntervalBaseline.ToString("R", CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_recovery_seconds", csvIntervalRecovery.ToString("R", CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_planned_bursts_per_repeat", csvIntervalPlannedBursts.ToString(CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_repetitions", csvRunRepetitions.ToString(CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_frame_capacity", csvFrames.Length.ToString(CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_event_count", csvSpawner.IntervalEventCount.ToString(CultureInfo.InvariantCulture));
        WriteCsvSetting(writer, "interval_policy", "First burst at t=0, fixed schedule t<duration; max one burst per frame, no dropped catch-up ticks. Return each ingredient after its observed activation plus lifetime; drain naturally before Recovery.");
        WriteCsvSetting(writer, "interval_sampling", "Continuous completed frames; batch Counts/Sample Frames are ignored. count means ingredients per burst; verified_objects means concurrent active count. Latency is client request to observed activation, not server/network latency.");
        WriteCsvSetting(writer, "interval_bookkeeping", "Frame/event buffers and collections allocated before Baseline; same capacities in both backends. Verification/tracking CPU is test overhead. No forced GC; no file IO/formatting while sampling.");
    }

    private void SaveIntervalEvents()
    {
        using (var writer = NewCsvWriter("events.csv"))
        {
            writer.WriteLine("mode,repeat,burst,network_id,instance_id,outcome,submitted_frame,activated_frame,returned_frame,scheduled_time,submitted_time,activated_time,returned_time,schedule_lateness_ms,request_to_active_ms,held_seconds,reused_after_return");
            for (int i = 0; i < csvSpawner.IntervalEventCount; i++)
            {
                var e = csvSpawner.GetIntervalEvent(i);
                writer.Write(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7},{8}", ActiveBackend,
                    e.Repeat, e.Burst, e.NetworkId, e.InstanceId,
                    e.Cancelled ? "Cancelled" : e.ReturnedFrame > 0 ? "Returned" : e.ActivatedFrame > 0 ? "Active" : "Pending",
                    e.SubmittedFrame, e.ActivatedFrame, e.ReturnedFrame));
                WriteCsvNumber(writer, e.ScheduledAt); WriteCsvNumber(writer, e.SubmittedAt);
                WriteCsvNumber(writer, e.ActivatedFrame > 0 ? e.ActivatedAt : double.NaN);
                WriteCsvNumber(writer, e.ReturnedFrame > 0 ? e.ReturnedAt : double.NaN);
                WriteCsvNumber(writer, (e.SubmittedAt - e.ScheduledAt) * 1000);
                WriteCsvNumber(writer, e.ActivatedFrame > 0 ? (e.ActivatedAt - e.SubmittedAt) * 1000 : double.NaN);
                WriteCsvNumber(writer, e.ActivatedFrame > 0 && e.ReturnedFrame > 0 ? e.ReturnedAt - e.ActivatedAt : double.NaN);
                writer.WriteLine(e.ReusedAfterReturn ? ",1" : ",0");
            }
        }
        // Export only after measurement: sorting and lists cannot perturb the recorded frames.
        using var summary = NewCsvWriter("interval_summary.csv");
        summary.WriteLine("mode,repeat,run_status,submitted,activated,returned,reused_after_return,peak_active,request_to_active_ms_p50,request_to_active_ms_p95,request_to_active_ms_p99,request_to_active_ms_max,schedule_lateness_ms_p95,schedule_lateness_ms_max,held_seconds_min,held_seconds_max,frame_interval_ms_p99,main_thread_ms_p99,main_thread_ms_max,main_thread_over_16_67ms_frames,main_thread_over_33_33ms_frames");
        int mainMetric = Array.FindIndex(CsvMetrics, x => x.Column == "main_thread_ms");
        for (int repeat = 1; repeat <= csvRunRepetitions; repeat++)
        {
            var latencies = new List<double>(); var late = new List<double>(); var held = new List<double>();
            var main = new List<double>(); var intervals = new List<double>();
            int submitted = 0, activated = 0, returned = 0, reused = 0, peak = 0, over60 = 0, over30 = 0;
            for (int i = 0; i < csvSpawner.IntervalEventCount; i++)
            {
                var e = csvSpawner.GetIntervalEvent(i);
                if (e.Repeat != repeat) continue;
                submitted++; late.Add((e.SubmittedAt - e.ScheduledAt) * 1000);
                if (e.ActivatedFrame > 0) { activated++; latencies.Add((e.ActivatedAt - e.SubmittedAt) * 1000); }
                if (e.ReturnedFrame > 0) returned++;
                if (e.ReusedAfterReturn) reused++;
                if (e.ActivatedFrame > 0 && e.ReturnedFrame > 0 && !e.Cancelled) held.Add(e.ReturnedAt - e.ActivatedAt);
            }
            if (submitted == 0) continue;
            for (int i = 0; i < csvWrittenFrames; i++)
            {
                CsvFrame f = csvFrames[i];
                if (f.Repeat != repeat || (f.Stage != CsvStage.IntervalRunning && f.Stage != CsvStage.IntervalDraining)) continue;
                peak = Math.Max(peak, f.Objects);
                double value = csvValues[i * CsvMetrics.Length + mainMetric];
                if (!double.IsNaN(value)) { main.Add(value); if (value > 1000.0 / 60) over60++; if (value > 1000.0 / 30) over30++; }
                intervals.Add(csvValues[i * CsvMetrics.Length]);
            }
            latencies.Sort(); late.Sort(); held.Sort(); main.Sort(); intervals.Sort();
            summary.Write(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7}", ActiveBackend, repeat, CaptureState, submitted, activated, returned, reused, peak));
            foreach (double q in new[] { .5, .95, .99, 1 }) WriteCsvNumber(summary, Quantile(latencies, q));
            WriteCsvNumber(summary, Quantile(late, .95)); WriteCsvNumber(summary, Quantile(late, 1));
            WriteCsvNumber(summary, held.Count > 0 ? held[0] : double.NaN); WriteCsvNumber(summary, Quantile(held, 1));
            WriteCsvNumber(summary, Quantile(intervals, .99)); WriteCsvNumber(summary, Quantile(main, .99)); WriteCsvNumber(summary, Quantile(main, 1));
            summary.WriteLine($",{over60},{over30}");
        }
    }

    private static double Quantile(List<double> sorted, double q) => sorted.Count == 0 ? double.NaN : sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * q) - 1)];
}
