using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

// Part of the existing test component, not a game manager. No file IO or formatting while sampling.
public sealed partial class TEST_ECSComparison
{
    public enum CsvScenario { BatchLifecycle, IntervalLifecycle }
    [SerializeField] private CsvScenario csvScenario;
    public enum CsvPoolPreparation { Cold, Prewarmed }
    [Header("Automatic CSV test (spawn > return > reuse)")]
    [Tooltip("Cold matches earlier CSV runs. Prewarmed prepares the case quantity before Baseline; preparation has its own measured stage.")]
    [SerializeField] private CsvPoolPreparation csvPoolPreparation;
    [SerializeField] private int[] csvCounts = { 50, 100, 200, 500 };
    [SerializeField, Min(1)] private int csvRepetitions = 1;
    [SerializeField, Min(10)] private int csvSampleFrames = 120;
    [SerializeField, Min(0f)] private float csvSettleSeconds = 2f;
    [SerializeField, Min(1024)] private int csvFrameCapacity = 16384;
    [SerializeField] private bool csvStartOnReady;
    [SerializeField, HideInInspector] private string csvGroupId;

    public enum CsvState { Idle, Running, Completed, Cancelled, Failed }
    private enum CsvStage { Preparing, Prewarm, PrewarmSettling, Baseline, FirstSpawn, SpawnSettling, Spawned, Return, ReturnSettling, Returned, ReuseSpawn, ReuseSettling, Reused, IntervalRunning, IntervalDraining, IntervalRecovered }
    [Flags] private enum ExportStats { Mean = 1, P95 = 2, Max = 4, Sum = 8, Last = 16, P99 = 32 }
    private struct CsvFrame
    {
        public int Frame, Case, Count, Repeat, Objects, Entities, Reused;
        public int PairedCapacity, PairedCreated, PairedReused;
        public int IntervalPending, IntervalSubmitted, IntervalActivated, IntervalReturned;
        public CsvStage Stage;
    }
    private readonly struct CsvMetric
    {
        public readonly string Column, Marker;
        public readonly double Scale;
        public readonly ExportStats Stats;
        public readonly bool IsTiming;
        public CsvMetric(string column, string marker, bool timing, ExportStats stats)
        { Column = column; Marker = marker; IsTiming = timing; Scale = timing ? 1e-6 : 1; Stats = stats; }
    }
    private static readonly CsvMetric[] CsvMetrics =
    {
        new("frame_interval_ms", null, false, ExportStats.Mean | ExportStats.P95 | ExportStats.P99 | ExportStats.Max),
        new("main_thread_ms", "Main Thread", true, ExportStats.Mean | ExportStats.P95 | ExportStats.P99 | ExportStats.Max),
        new("player_loop_ms", "PlayerLoop", true, ExportStats.Mean | ExportStats.Max),
        new("script_update_ms", "Update.ScriptRunBehaviourUpdate", true, ExportStats.Mean | ExportStats.Max),
        new("script_lateupdate_ms", "PreLateUpdate.ScriptRunBehaviourLateUpdate", true, ExportStats.Mean),
        new("script_fixedupdate_ms", "FixedUpdate.ScriptRunBehaviourFixedUpdate", true, ExportStats.Mean),
        new("physics_simulate_ms", "Physics.Simulate", true, ExportStats.Mean | ExportStats.Max),
        new("gc_incremental_ms", "GarbageCollector.CollectIncremental", true, ExportStats.Mean | ExportStats.Max | ExportStats.Sum),
        new("gc_alloc_bytes", "GC Allocated In Frame", false, ExportStats.Mean | ExportStats.Max | ExportStats.Sum),
        new("gc_used_bytes", "GC Used Memory", false, ExportStats.Last | ExportStats.Max),
        new("total_allocated_bytes", TEST_PerformanceSpawner.AllocatedMemoryCounterName, false, ExportStats.Last | ExportStats.Max),
        new("object_count", "Object Count", false, ExportStats.Last | ExportStats.Max),
        new("mesh_bytes", "Mesh Memory", false, ExportStats.Last | ExportStats.Max),
        new("submit_ms", "TEST.Comparison.SubmitBatch", true, ExportStats.Sum | ExportStats.Max),
        new("spawn_process_ms", "Ingredient.SpawnRequest.Process", true, ExportStats.Sum | ExportStats.Max),
        new("go_spawn_ms", "TEST.Comparison.GameObjectOnly.Spawn", true, ExportStats.Sum | ExportStats.Max),
        new("central_spawn_ms", "Ingredient.GO.ProcessRequests", true, ExportStats.Sum | ExportStats.Max),
        new("pool_spawn_ms", "Ingredient.ObjectPool.Spawn", true, ExportStats.Sum | ExportStats.Max),
        new("pool_create_ms", "Ingredient.ObjectPool.Create", true, ExportStats.Sum | ExportStats.Max),
        new("prewarm_ms", "TEST.Performance.Prewarm", true, ExportStats.Sum | ExportStats.Max),
        new("entity_record_ms", "Ingredient.Entity.RecordCommands", true, ExportStats.Sum),
        new("entity_playback_ms", "Ingredient.Entity.Playback", true, ExportStats.Sum),
        new("batch_process_ms", "Ingredient.SpawnBatch.Process", true, ExportStats.Sum | ExportStats.Max),
        new("pooled_entity_create_ms", "Ingredient.EntityPool.Create", true, ExportStats.Sum | ExportStats.Max),
        new("pooled_entity_activate_ms", "Ingredient.EntityPool.Activate", true, ExportStats.Sum),
        new("pooled_entity_return_ms", "Ingredient.EntityPool.Return", true, ExportStats.Sum),
        new("clear_ms", "TEST.Performance.Clear", true, ExportStats.Sum),
        new("conveyor_ms", "Ingredient.Conveyor.Update", true, ExportStats.Mean | ExportStats.Max),
        new("conveyor_loop_ms", "TEST.Performance.ConveyorLoop.Check", true, ExportStats.Mean),
        new("interval_return_ms", "TEST.Interval.ReturnToPool", true, ExportStats.Sum | ExportStats.Max)
    };
    private TEST_PerformanceSpawner csvSpawner;
    private TEST_PerformanceBootstrap csvBootstrap;
    private ProfilerRecorder[] csvRecorders;
    private string[] csvMetricSources;
    private CsvFrame[] csvFrames;
    private double[] csvValues;
    private CsvFrame pendingCsvFrame;
    private bool csvFramePending;
    private int csvWrittenFrames, csvCase, csvStageFrames, csvOperationCompleteFrame;
    private int[] csvRunCounts;
    private int csvRunRepetitions, csvRunSampleFrames;
    private CsvPoolPreparation csvRunPoolPreparation;
    private int originalSpawnCount;
    private int csvCreatedBeforeSpawn, csvReusedBeforeReuse;
    private bool csvHasOriginalCount;
    private double csvNextStageTime, csvDeadline, csvFrameStart;
    private CsvStage csvStage;
    private string csvSettings;
    public CsvState CaptureState { get; private set; }
    public bool IsCsvRunning => CaptureState == CsvState.Running;
    public string CaptureStatus { get; private set; } = "Idle";
    public string LastCsvDirectory { get; private set; }
    public int RecordedCsvFrames => csvWrittenFrames;
    public int CsvCaseNumber => csvCase + 1;
    public string CaptureStageName => csvStage.ToString();

    // Editor automation sets these in memory only; original fields are restored after both sessions.
    public void ConfigureAutomaticCsv(Backend selectedBackend, bool start, string groupId)
    { backend = selectedBackend; csvStartOnReady = start; csvGroupId = groupId; }

    private void Start()
    {
        csvSpawner = GetComponent<TEST_PerformanceSpawner>();
        csvBootstrap = GetComponent<TEST_PerformanceBootstrap>();
        if (csvStartOnReady) BeginCsvCapture();
    }

    public void BeginCsvCapture()
    {
        if (!Application.isPlaying || IsCsvRunning) return;
        // A rejected second run must never export old rows over the previous successful result.
        csvFrames = null;
        csvWrittenFrames = 0;
        LastCsvDirectory = null;
        csvHasOriginalCount = false;
        try
        {
            csvRunInterval = csvScenario == CsvScenario.IntervalLifecycle;
            if (!IsInitialized || InitializationError != null || csvSpawner == null || csvBootstrap == null)
                throw new InvalidOperationException(InitializationError ?? "Comparison components are not initialized.");
            if (csvSpawner.AutoSpawn || csvSpawner.HasBatch || csvSpawner.IsBusy)
                throw new InvalidOperationException("Disable Auto Spawn and start with no active batch before automatic CSV capture.");
            if ((!csvRunInterval && (csvCounts == null || csvCounts.Length == 0)) || csvRepetitions < 1 || csvSampleFrames < 10 || csvSettleSeconds < 0)
                throw new InvalidOperationException("Invalid CSV counts, repetitions, sample frames, settle time or capacity.");
            foreach (int count in csvRunInterval ? new[] { intervalSpawnCount } : csvCounts)
                if (count < 0 || count > 500) throw new InvalidOperationException("CSV counts must be between 0 and 500.");
            if (csvRunInterval) PrepareIntervalConfiguration();
            int frameCapacity = csvRunInterval ? intervalFrameCapacity : csvFrameCapacity;
            long minimumFrames = csvRunInterval ? (long)csvRepetitions * (csvIntervalPlannedBursts + 6L) : (long)csvCounts.Length * csvRepetitions * (csvSampleFrames * 4L + 16);
            if (minimumFrames > frameCapacity || frameCapacity < 1024 || frameCapacity > 1000000)
                throw new InvalidOperationException("Increase CSV Frame Capacity for these settings (maximum 1,000,000).");
            csvRunCounts = csvRunInterval ? new[] { intervalSpawnCount } : (int[])csvCounts.Clone();
            csvRunRepetitions = csvRepetitions;
            csvRunSampleFrames = csvSampleFrames;
            csvRunPoolPreparation = csvRunInterval ? CsvPoolPreparation.Prewarmed : csvPoolPreparation;
            originalSpawnCount = csvSpawner.SpawnCount;
            csvHasOriginalCount = true;
            csvFrames = new CsvFrame[frameCapacity];
            csvValues = new double[checked(frameCapacity * CsvMetrics.Length)];
            csvRecorders = new ProfilerRecorder[CsvMetrics.Length];
            csvMetricSources = new string[CsvMetrics.Length];
            csvSettings = JsonUtility.ToJson(csvSpawner);
            string group = string.IsNullOrWhiteSpace(csvGroupId) ? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) : csvGroupId;
            if (group.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || group == "." || group == "..")
                throw new InvalidOperationException("Invalid CSV group ID.");
            LastCsvDirectory = Path.Combine(Application.persistentDataPath, "PerformanceTests", csvRunInterval ? "ECSComparisonInterval" : "ECSComparison", group, ActiveBackend.ToString());
            if (Directory.Exists(LastCsvDirectory)) LastCsvDirectory += "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            // All managed buffers, metadata strings and directories are prepared before Baseline.
            Directory.CreateDirectory(LastCsvDirectory);
            csvWrittenFrames = csvCase = 0;
            csvFramePending = false;
            CaptureState = CsvState.Running;
            CaptureStatus = "Preparing counters and pool";
            csvStage = CsvStage.Preparing;
            csvDeadline = Time.realtimeSinceStartupAsDouble + 90;
            csvNextStageTime = double.PositiveInfinity;
        }
        catch (Exception exception) { EndCsvCapture(CsvState.Failed, exception.Message); }
    }

    private void Update()
    {
        if (!IsCsvRunning) return;
        try
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (csvFramePending)
            {
                // LastValue is from the completed frame, never from the action about to run.
                ReadCsvFrame(now);
                csvFramePending = false;
            }
            if (csvBootstrap.HasFailed || csvSpawner.MemoryStage == TEST_PerformanceSpawner.MemoryTestStage.Failed)
                throw new InvalidOperationException(csvBootstrap.HasFailed ? csvBootstrap.Status : csvSpawner.Status);
            if (now > csvDeadline) throw new TimeoutException("CSV stage timed out: " + csvStage);
            if (!csvSpawner.IsReady) return;
            if (csvRunInterval) AdvanceIntervalCsv(now); else AdvanceCsvStage(now);
            if (!IsCsvRunning) return;
            if (IsMeasuredStage(csvStage))
            {
                pendingCsvFrame = new CsvFrame { Frame = Time.frameCount, Case = csvCase, Count = CsvCount,
                    Repeat = csvCase % csvRunRepetitions + 1, Stage = csvStage };
                csvFrameStart = now;
                csvFramePending = true;
            }
        }
        catch (Exception exception) { EndCsvCapture(CsvState.Failed, exception.Message); }
    }

    private void LateUpdate()
    {
        if (!IsCsvRunning || !csvFramePending) return;
        pendingCsvFrame.Objects = csvSpawner.VerifiedObjectCount;
        pendingCsvFrame.Entities = csvSpawner.VerifiedEntityCount;
        pendingCsvFrame.Reused = csvSpawner.ReusedObjectCount;
        if (csvRunInterval)
        {
            pendingCsvFrame.Objects = csvSpawner.IntervalActiveCount;
            pendingCsvFrame.Entities = csvSpawner.IntervalEntityCount;
            pendingCsvFrame.Reused = csvSpawner.IntervalReusedTotal;
            pendingCsvFrame.IntervalPending = csvSpawner.IntervalPendingCount;
            pendingCsvFrame.IntervalSubmitted = csvSpawner.IntervalSubmittedTotal;
            pendingCsvFrame.IntervalActivated = csvSpawner.IntervalActivatedTotal;
            pendingCsvFrame.IntervalReturned = csvSpawner.IntervalReturnedTotal;
        }
        pendingCsvFrame.PairedCapacity = csvSpawner.PairedEntityCapacity;
        pendingCsvFrame.PairedCreated = csvSpawner.PairedEntitiesCreated;
        pendingCsvFrame.PairedReused = csvSpawner.PairedEntitiesReused;
    }

    private int CsvCount => csvRunCounts[csvCase / csvRunRepetitions];
    private static bool IsMeasuredStage(CsvStage stage) => stage == CsvStage.Prewarm || stage == CsvStage.Baseline || stage == CsvStage.FirstSpawn ||
        stage == CsvStage.Spawned || stage == CsvStage.Return || stage == CsvStage.Returned || stage == CsvStage.ReuseSpawn || stage == CsvStage.Reused ||
        stage == CsvStage.IntervalRunning || stage == CsvStage.IntervalDraining || stage == CsvStage.IntervalRecovered;

    private void AdvanceCsvStage(double now)
    {
        if (csvStage == CsvStage.Preparing)
        {
            if (!csvSpawner.CanSpawn) return;
            if (double.IsPositiveInfinity(csvNextStageTime))
            {
                csvSpawner.SpawnCount = CsvCount;
                csvSpawner.EmptyIngredientPool();
                if (csvSpawner.MemoryStage == TEST_PerformanceSpawner.MemoryTestStage.Failed) throw new InvalidOperationException(csvSpawner.Status);
                CreateCsvRecorders();
                csvNextStageTime = now + csvSettleSeconds;
                csvOperationCompleteFrame = Time.frameCount;
            }
            if (now >= csvNextStageTime && Time.frameCount >= csvOperationCompleteFrame + 3)
            {
                if (csvRunPoolPreparation == CsvPoolPreparation.Prewarmed)
                {
                    SetCsvStage(CsvStage.Prewarm);
                    if (!csvSpawner.PrewarmPool(CsvCount)) throw new InvalidOperationException(csvSpawner.Status);
                }
                else SetCsvStage(CsvStage.Baseline);
            }
        }
        else if (csvStage == CsvStage.Prewarm) FinishCsvOperation(now, CsvStage.PrewarmSettling);
        else if (csvStage == CsvStage.PrewarmSettling && now >= csvNextStageTime) SetCsvStage(CsvStage.Baseline);
        else if (csvStage == CsvStage.Baseline && csvStageFrames >= csvRunSampleFrames)
        {
            csvCreatedBeforeSpawn = csvSpawner.PairedEntitiesCreated;
            SetCsvStage(CsvStage.FirstSpawn); csvSpawner.SpawnConfigured();
        }
        else if (csvStage == CsvStage.FirstSpawn && SpawnVerified(false)) FinishCsvOperation(now, CsvStage.SpawnSettling);
        else if (csvStage == CsvStage.SpawnSettling && now >= csvNextStageTime) SetCsvStage(CsvStage.Spawned);
        else if (csvStage == CsvStage.Spawned && csvStageFrames >= csvRunSampleFrames)
        { SetCsvStage(CsvStage.Return); if (csvSpawner.HasBatch) csvSpawner.ClearSpawned(); }
        else if (csvStage == CsvStage.Return && !csvSpawner.IsBusy && !csvSpawner.HasBatch)
        {
            if (csvSpawner.ReturnedCount != CsvCount) throw new InvalidOperationException("CSV return count mismatch.");
            FinishCsvOperation(now, CsvStage.ReturnSettling);
        }
        else if (csvStage == CsvStage.ReturnSettling && now >= csvNextStageTime) SetCsvStage(CsvStage.Returned);
        else if (csvStage == CsvStage.Returned && csvStageFrames >= csvRunSampleFrames)
        {
            csvCreatedBeforeSpawn = csvSpawner.PairedEntitiesCreated;
            csvReusedBeforeReuse = csvSpawner.PairedEntitiesReused;
            SetCsvStage(CsvStage.ReuseSpawn); if (CsvCount > 0) csvSpawner.RespawnReturnedBatch(); else csvSpawner.SpawnConfigured();
        }
        else if (csvStage == CsvStage.ReuseSpawn && SpawnVerified(true)) FinishCsvOperation(now, CsvStage.ReuseSettling);
        else if (csvStage == CsvStage.ReuseSettling && now >= csvNextStageTime) SetCsvStage(CsvStage.Reused);
        else if (csvStage == CsvStage.Reused && csvStageFrames >= csvRunSampleFrames)
        {
            csvCase++;
            if (csvCase == csvRunCounts.Length * csvRunRepetitions)
            { EndCsvCapture(CsvState.Completed, "Completed"); return; }
            // Case cleanup is preparation, outside the recorded Return stage.
            if (csvSpawner.HasBatch) csvSpawner.ClearSpawned();
            SetCsvStage(CsvStage.Preparing);
            csvNextStageTime = double.PositiveInfinity;
        }
    }

    private bool SpawnVerified(bool reused)
    {
        if (csvSpawner.IsBusy) return false;
        if (csvSpawner.VerifiedObjectCount != CsvCount || csvSpawner.VerifiedEntityCount != (UsesECS ? CsvCount : 0) ||
            (reused && csvSpawner.ReusedObjectCount != CsvCount)) throw new InvalidOperationException("CSV spawn/reuse count mismatch.");
        if (UsesPairedPooling)
        {
            if (csvSpawner.PairedEntityCapacity != CsvCount) throw new InvalidOperationException("Paired Entity capacity mismatch.");
            if ((reused || csvRunPoolPreparation == CsvPoolPreparation.Prewarmed) && csvSpawner.PairedEntitiesCreated != csvCreatedBeforeSpawn)
                throw new InvalidOperationException("An Entity was created during a prepared/reused spawn.");
            if (reused && csvSpawner.PairedEntitiesReused - csvReusedBeforeReuse != CsvCount)
                throw new InvalidOperationException("Entity reuse count mismatch.");
        }
        return true;
    }

    private void FinishCsvOperation(double now, CsvStage next)
    {
        // Keep the completion frame and one trailing frame inside the operation window.
        if (csvOperationCompleteFrame < 0) csvOperationCompleteFrame = Time.frameCount;
        if (Time.frameCount <= csvOperationCompleteFrame + 1) return;
        SetCsvStage(next);
        csvNextStageTime = now + csvSettleSeconds;
    }

    private void SetCsvStage(CsvStage stage)
    {
        csvStage = stage;
        csvStageFrames = 0;
        csvOperationCompleteFrame = -1;
        csvDeadline = Time.realtimeSinceStartupAsDouble + Math.Max(60, csvSettleSeconds + 30);
        CaptureStatus = stage.ToString(); // Once per transition, never per-frame formatting.
    }

    private void CreateCsvRecorders()
    {
        // Marker names can become available after ECS/pool initialization. Resolve once per case.
        var available = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(available);
        for (int m = 1; m < CsvMetrics.Length; m++)
        {
            if (csvRecorders[m].Valid) continue;
            foreach (var handle in available)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (description.Name != CsvMetrics[m].Marker) continue;
                var expected = CsvMetrics[m].IsTiming ? ProfilerMarkerDataUnit.TimeNanoseconds :
                    (CsvMetrics[m].Column == "object_count" ? ProfilerMarkerDataUnit.Count : ProfilerMarkerDataUnit.Bytes);
                if (description.UnitType != expected) continue;
                var options = ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately;
                if (CsvMetrics[m].IsTiming) options |= ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                csvRecorders[m] = new ProfilerRecorder(handle, 1, options);
                csvMetricSources[m] = description.Name;
                break;
            }
        }
        csvMetricSources[0] = "Monotonic elapsed time between capture Updates (includes Editor/pacing)";
    }

    private void ReadCsvFrame(double now)
    {
        if (csvWrittenFrames == csvFrames.Length) throw new InvalidOperationException(csvRunInterval ? "CSV buffer full. Increase Interval Frame Capacity." : "CSV buffer full. Increase CSV Frame Capacity.");
        csvFrames[csvWrittenFrames] = pendingCsvFrame;
        int offset = csvWrittenFrames * CsvMetrics.Length;
        csvValues[offset] = (now - csvFrameStart) * 1000;
        for (int m = 1; m < CsvMetrics.Length; m++)
        {
            var recorder = csvRecorders[m];
            // SumAllSamplesInFrame commits a completed-frame value. Keep collection running:
            // stopping inside Update would discard already-open Main Thread/PlayerLoop scopes.
            csvValues[offset + m] = recorder.Valid
                ? (recorder.Count > 0 ? recorder.LastValue * CsvMetrics[m].Scale : (CsvMetrics[m].IsTiming ? 0 : double.NaN))
                : double.NaN;
        }
        csvWrittenFrames++;
        csvStageFrames++;
    }

    public void CancelCsvCapture() { if (IsCsvRunning) EndCsvCapture(CsvState.Cancelled, "Cancelled; partial data saved"); }

    private void EndCsvCapture(CsvState result, string message)
    {
        CaptureState = result;
        CaptureStatus = message;
        csvFramePending = false;
        if (csvRecorders != null)
            for (int m = 0; m < csvRecorders.Length; m++) csvRecorders[m].Dispose();
        csvRecorders = null;
        if (csvRunInterval && csvSpawner != null) csvSpawner.StopIntervalTracking(result != CsvState.Completed);
        if (csvHasOriginalCount && csvSpawner != null) csvSpawner.SpawnCount = originalSpawnCount;
        // IO, sorting and string formatting start only after every recorder has stopped.
        if (csvFrames != null && csvWrittenFrames > 0 && LastCsvDirectory != null)
        {
            try { SaveCsvFiles(); if (csvRunInterval) SaveIntervalEvents(); }
            catch (Exception exception) { CaptureState = CsvState.Failed; CaptureStatus = "CSV write failed: " + exception.Message; }
        }
        Debug.Log($"[ECSComparison CSV] {CaptureState}: {CaptureStatus}. {LastCsvDirectory}", this);
    }

    private void OnDisable() { if (IsCsvRunning) EndCsvCapture(CsvState.Cancelled, "Play stopped/component disabled; partial data saved"); }

    private void SaveCsvFiles()
    {
        using (var writer = NewCsvWriter("frames.csv"))
        {
            writer.Write("mode,ingredient_id,conveyor,case,count,repeat,stage,frame,verified_objects,verified_entities,reused_objects,paired_entity_capacity,paired_entity_created_total,paired_entity_reused_total");
            foreach (var metric in CsvMetrics) writer.Write("," + metric.Column);
            if (csvRunInterval) writer.Write(",pending_requests,submitted_total,activated_total,returned_total");
            writer.WriteLine();
            for (int i = 0; i < csvWrittenFrames; i++)
            {
                WriteCsvIdentity(writer, csvFrames[i]);
                writer.Write($",{csvFrames[i].Frame},{csvFrames[i].Objects},{csvFrames[i].Entities},{csvFrames[i].Reused}");
                WritePairedCounts(writer, csvFrames[i]);
                for (int m = 0; m < CsvMetrics.Length; m++) WriteCsvNumber(writer, csvValues[i * CsvMetrics.Length + m]);
                if (csvRunInterval) writer.Write($",{csvFrames[i].IntervalPending},{csvFrames[i].IntervalSubmitted},{csvFrames[i].IntervalActivated},{csvFrames[i].IntervalReturned}");
                writer.WriteLine();
            }
        }
        using (var writer = NewCsvWriter("summary.csv"))
        {
            writer.Write("mode,ingredient_id,conveyor,case,count,repeat,stage,frames,run_status,paired_entity_capacity_last,paired_entity_created_total_last,paired_entity_reused_total_last");
            foreach (var metric in CsvMetrics)
                foreach (ExportStats stat in new[] { ExportStats.Mean, ExportStats.P95, ExportStats.P99, ExportStats.Max, ExportStats.Sum, ExportStats.Last })
                    if ((metric.Stats & stat) != 0) writer.Write("," + metric.Column + "_" + stat.ToString().ToLowerInvariant());
            writer.WriteLine();
            var scratch = new double[csvWrittenFrames];
            int start = 0;
            while (start < csvWrittenFrames)
            {
                CsvFrame identity = csvFrames[start];
                int end = start + 1;
                while (end < csvWrittenFrames && csvFrames[end].Case == identity.Case && csvFrames[end].Stage == identity.Stage) end++;
                WriteCsvIdentity(writer, identity);
                writer.Write($",{end - start},{CaptureState}");
                WritePairedCounts(writer, csvFrames[end - 1]);
                for (int m = 0; m < CsvMetrics.Length; m++)
                {
                    int n = 0;
                    double sum = 0, max = double.MinValue, last = double.NaN;
                    for (int i = start; i < end; i++)
                    {
                        double value = csvValues[i * CsvMetrics.Length + m];
                        if (double.IsNaN(value)) continue;
                        scratch[n++] = value; sum += value; max = Math.Max(max, value); last = value;
                    }
                    Array.Sort(scratch, 0, n);
                    foreach (ExportStats stat in new[] { ExportStats.Mean, ExportStats.P95, ExportStats.P99, ExportStats.Max, ExportStats.Sum, ExportStats.Last })
                    {
                        if ((CsvMetrics[m].Stats & stat) == 0) continue;
                        double value = n == 0 ? double.NaN : stat == ExportStats.Mean ? sum / n : stat == ExportStats.P95 ? scratch[Math.Max(0, (int)Math.Ceiling(n * .95) - 1)] :
                            stat == ExportStats.P99 ? scratch[Math.Max(0, (int)Math.Ceiling(n * .99) - 1)] :
                            stat == ExportStats.Max ? max : stat == ExportStats.Sum ? sum : last;
                        WriteCsvNumber(writer, value);
                    }
                }
                writer.WriteLine();
                start = end;
            }
        }
        using (var writer = NewCsvWriter("settings.csv"))
        {
            writer.WriteLine("key,value");
            WriteCsvSetting(writer, "status", CaptureState.ToString());
            WriteCsvSetting(writer, "message", CaptureStatus);
            WriteCsvSetting(writer, "unity", Application.unityVersion);
            WriteCsvSetting(writer, "editor", Application.isEditor.ToString());
            WriteCsvSetting(writer, "batch_mode", Application.isBatchMode.ToString());
            WriteCsvSetting(writer, "mode", ActiveBackend.ToString());
            WriteCsvSetting(writer, "scenario", csvRunInterval ? "IntervalLifecycle" : "BatchLifecycle");
            if (csvRunInterval) WriteIntervalSettings(writer);
            WriteCsvSetting(writer, "spawn_preparation_version", ObjectPoolManager.SpawnPreparationVersion);
            WriteCsvSetting(writer, "pool_preparation", csvRunPoolPreparation.ToString());
            WriteCsvSetting(writer, "spawn_pipeline", UsesCentralized ? IngredientSpawnProcessor.PipelineVersion :
                UsesPairedPooling ? "BatchedPooled_v1: shared request buffer, fixed archetype, GameObject-paired Entity reuse; experimental comparison scene opt-in" : "PerRequestEntity_v1 (or GameObjectOnly)");
            WriteCsvSetting(writer, "ecs_scope", UsesECS ? "Historical ECS baseline: explicit test opt-in only" :
                UsesCentralized ? "Production default World retained; no ingredient/request Entities or ingredient ECS systems" : "Historical GO baseline: no default ECS World");
            WriteCsvSetting(writer, "production_prewarm_in_capture", csvRunInterval ? "Disabled: Interval Prewarm Count is prepared before each repeat" : "Disabled: CSV Prewarm stage prepares the exact case quantity in every mode");
            WriteCsvSetting(writer, "paired_counts", "Blank outside BatchedPooled mode. Capacity includes inactive Entities; created/reused are cumulative since this World started. Prewarm activation counts as reuse. Compare stage last values within each case, not across independent runs. Inactive Entities have IngredientPooledActive disabled and NetworkID=0; queries must explicitly include the active component.");
            WriteCsvSetting(writer, "prewarm_policy", csvRunInterval ? "Interval Prewarm Count prepared before each repeat; capacity shortfall uses the existing pool growth path and is measured. Prewarm is separate from Running." : "Prewarmed: case quantity prepared in Prewarm before Baseline. Cold: empty pool. FirstSpawn excludes prewarm; compare Prewarm separately, including its memory and GC cost.");
            WriteCsvSetting(writer, "random_seed", randomSeed.ToString(CultureInfo.InvariantCulture));
            WriteCsvSetting(writer, "v_sync", QualitySettings.vSyncCount.ToString(CultureInfo.InvariantCulture));
            WriteCsvSetting(writer, "target_fps", Application.targetFrameRate.ToString(CultureInfo.InvariantCulture));
            WriteCsvSetting(writer, "graphics_device", SystemInfo.graphicsDeviceName);
            WriteCsvSetting(writer, "resolution", Screen.width + "x" + Screen.height);
            WriteCsvSetting(writer, "sample_frames", csvRunSampleFrames.ToString(CultureInfo.InvariantCulture));
            WriteCsvSetting(writer, "settle_seconds", csvSettleSeconds.ToString(CultureInfo.InvariantCulture));
            WriteCsvSetting(writer, "spawner_settings", csvSettings);
            WriteCsvSetting(writer, "timing_scope", "Main-thread inclusive markers; frame interval/Main Thread/PlayerLoop include pacing or Editor work. Script fields are named callbacks, not the entire Scripts chart. Marker parent/child sums overlap.");
            for (int m = 0; m < CsvMetrics.Length; m++)
                WriteCsvSetting(writer, CsvMetrics[m].Column, csvMetricSources[m] ?? "UNAVAILABLE (blank, not zero)");
        }
    }

    private StreamWriter NewCsvWriter(string name) => new(Path.Combine(LastCsvDirectory, name), false, new UTF8Encoding(true));
    private static void WritePairedCounts(StreamWriter writer, CsvFrame frame)
    {
        WriteCsvNumber(writer, frame.PairedCapacity < 0 ? double.NaN : frame.PairedCapacity);
        WriteCsvNumber(writer, frame.PairedCreated < 0 ? double.NaN : frame.PairedCreated);
        WriteCsvNumber(writer, frame.PairedReused < 0 ? double.NaN : frame.PairedReused);
    }
    private void WriteCsvIdentity(StreamWriter writer, CsvFrame frame) => writer.Write(string.Format(CultureInfo.InvariantCulture,
        "{0},{1},{2},{3},{4},{5},{6}", ActiveBackend, csvSpawner.IngredientId, csvSpawner.UseConveyor ? 1 : 0, frame.Case + 1, frame.Count, frame.Repeat, frame.Stage));
    private static void WriteCsvNumber(StreamWriter writer, double value)
    { writer.Write(','); if (!double.IsNaN(value)) writer.Write(value.ToString("0.######", CultureInfo.InvariantCulture)); }
    private static void WriteCsvSetting(StreamWriter writer, string key, string value)
    { writer.Write(key); writer.Write(",\""); writer.Write((value ?? "").Replace("\"", "\"\"")); writer.WriteLine("\""); }
}
