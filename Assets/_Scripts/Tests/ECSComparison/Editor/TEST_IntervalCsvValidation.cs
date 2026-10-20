using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TEST_IntervalCsvValidation
{
    private const string Key = "TEST.IntervalCsvValidation";
    private static double deadline;
    private static int phase;
    private static string firstOutput;
    static TEST_IntervalCsvValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        deadline = EditorApplication.timeSinceStartup + 300;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void RunBatchValidation() => Begin(false);
    public static void RunDriverValidation() => Begin(true);
    private static void Begin(bool driver)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity batch mode.");
        TEST_ECSComparisonSceneEditor.CreateForBatch();
        var target = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
        var settings = new SerializedObject(target);
        string mode = "GameObjectCentralized";
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ecsComparisonMode") mode = args[i + 1];
        settings.FindProperty("backend").enumValueIndex = driver ? 0 : (int)Enum.Parse(typeof(TEST_ECSComparison.Backend), mode);
        settings.FindProperty("csvScenario").enumValueIndex = driver ? 0 : 1;
        settings.FindProperty("csvRepetitions").intValue = 2;
        settings.FindProperty("csvSettleSeconds").floatValue = .05f;
        settings.FindProperty("csvStartOnReady").boolValue = !driver;
        settings.FindProperty("intervalSpawnSeconds").floatValue = .05f;
        settings.FindProperty("intervalSpawnCount").intValue = 2;
        settings.FindProperty("intervalLifetimeSeconds").floatValue = .12f;
        settings.FindProperty("intervalDurationSeconds").floatValue = .6f;
        settings.FindProperty("intervalPrewarmCount").intValue = 8;
        settings.FindProperty("intervalBaselineSeconds").floatValue = .05f;
        settings.FindProperty("intervalRecoverySeconds").floatValue = .05f;
        settings.FindProperty("intervalFrameCapacity").intValue = 32768;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var spawner = new SerializedObject(target.GetComponent<TEST_PerformanceSpawner>());
        spawner.FindProperty("autoSpawn").boolValue = false;
        spawner.FindProperty("useConveyor").boolValue = true;
        spawner.ApplyModifiedPropertiesWithoutUndo();
        SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".Driver", driver);
        deadline = EditorApplication.timeSinceStartup + 300;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        if (driver) TEST_ECSComparisonCsvRunner.RunIntervalBeforeAfter(); else EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Interval validation timed out.");
            if (SessionState.GetBool(Key + ".Driver", false))
            {
                if (TEST_ECSComparisonCsvRunner.IsRunning) return;
                string root = TEST_ECSComparisonCsvRunner.LastOutputDirectory;
                foreach (string mode in new[] { "HybridECS", "GameObjectCentralized" }) VerifyCapture(Path.Combine(root, mode), mode, 24, 2, true);
                var restored = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>());
                if (restored.FindProperty("backend").enumValueIndex != 0 || restored.FindProperty("csvScenario").enumValueIndex != 0 || restored.FindProperty("csvStartOnReady").boolValue)
                    throw new Exception("Interval runner did not restore Inspector fields.");
                Finish(true, "two fresh interval sessions; identical settings, CSV events/counters and original Inspector restored");
                return;
            }
            if (!EditorApplication.isPlaying) return;
            var target = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
            if (target.CaptureState == TEST_ECSComparison.CsvState.Failed) throw new Exception(target.CaptureStatus);
            var spawner = target.GetComponent<TEST_PerformanceSpawner>();
            if (phase == 0 && target.CaptureState == TEST_ECSComparison.CsvState.Completed)
            {
                VerifyCapture(target.LastCsvDirectory, target.ActiveBackend.ToString(), 24, 2, true);
                firstOutput = target.LastCsvDirectory;
                var settings = new SerializedObject(target);
                settings.FindProperty("intervalDurationSeconds").floatValue = 2f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                target.BeginCsvCapture(); phase = 1;
            }
            else if (phase == 1 && target.IntervalLiveCount > 0)
            {
                target.CancelCsvCapture();
                if (target.CaptureState != TEST_ECSComparison.CsvState.Cancelled || target.LastCsvDirectory == firstOutput ||
                    !File.ReadAllText(Path.Combine(target.LastCsvDirectory, "summary.csv")).Contains(",Cancelled,") ||
                    !File.ReadAllText(Path.Combine(target.LastCsvDirectory, "events.csv")).Contains(",Cancelled,"))
                    throw new Exception("Partial cancellation was not saved separately.");
                phase = 2;
            }
            else if (phase == 2 && spawner.CanSpawn)
            {
                if (ObjectPoolManager.Instance.activeObjDict.Count != 0 || UnityEngine.Object.FindFirstObjectByType<TEST_PerformanceBootstrap>().Router.catchableDics.Count != 0 || spawner.IntervalEntityCount != 0)
                    throw new Exception("Cancellation left active registration/Entities.");
                var settings = new SerializedObject(target);
                settings.FindProperty("intervalDurationSeconds").floatValue = .3f;
                settings.FindProperty("intervalPrewarmCount").intValue = 0;
                settings.FindProperty("csvRepetitions").intValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
                var spawnSettings = new SerializedObject(spawner);
                spawnSettings.FindProperty("useConveyor").boolValue = false;
                spawnSettings.ApplyModifiedPropertiesWithoutUndo();
                target.BeginCsvCapture(); phase = 3;
            }
            else if (phase == 3 && target.CaptureState == TEST_ECSComparison.CsvState.Completed)
            {
                VerifyCapture(target.LastCsvDirectory, target.ActiveBackend.ToString(), 12, 1, false);
                if (UnityEngine.Object.FindAnyObjectByType<Server.ServerManager>() != null) throw new Exception("Unexpected server");
                if (target.UsesCentralized && World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<IngredientSpawnSystem>() != null)
                    throw new Exception("GO test installed a legacy system");
                Finish(true, target.ActiveBackend + ": overlapping conveyor/floor bursts, timing, natural drain, actual pool reuse, pool growth, CSV p99/aggregates, cancellation/restart and no server");
            }
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }

    private static List<Dictionary<string, string>> Read(string path)
    {
        string[] lines = File.ReadAllLines(path);
        string[] headers = lines[0].TrimStart('\uFEFF').Split(',');
        var result = new List<Dictionary<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = lines[i].Split(',');
            if (values.Length != headers.Length) throw new Exception("Malformed CSV: " + path);
            var row = new Dictionary<string, string>();
            for (int j = 0; j < headers.Length; j++) row.Add(headers[j], values[j]);
            result.Add(row);
        }
        return result;
    }
    private static double N(Dictionary<string, string> r, string k) => double.Parse(r[k], CultureInfo.InvariantCulture);
    private static double Q(List<double> sorted, double q) => sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * q) - 1)];

    private static void VerifyCapture(string directory, string mode, int perRepeat, int repetitions, bool prepared)
    {
        var frames = Read(Path.Combine(directory, "frames.csv"));
        var summary = Read(Path.Combine(directory, "summary.csv"));
        var events = Read(Path.Combine(directory, "events.csv"));
        var totals = Read(Path.Combine(directory, "interval_summary.csv"));
        if (events.Count != perRepeat * repetitions || totals.Count != repetitions || summary.Count != 5 * repetitions)
            throw new Exception("Missing interval CSV stages/events");
        var ids = new HashSet<long>(); var frameIds = new HashSet<int>();
        var groups = new Dictionary<string, List<Dictionary<string, string>>>();
        bool grewPool = false, overlap = false;
        foreach (var f in frames)
        {
            if (!frameIds.Add((int)N(f, "frame"))) throw new Exception("Repeated frame");
            string key = f["case"] + ":" + f["stage"];
            if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new List<Dictionary<string, string>>());
            group.Add(f);
            if (f["stage"] == "IntervalRunning" || f["stage"] == "IntervalDraining")
            {
                if (prepared && N(f, "pool_create_ms") != 0) throw new Exception("Prepared interval spawn instantiated a GO");
                grewPool |= N(f, "pool_create_ms") > 0;
                overlap |= N(f, "verified_objects") > 2;
            }
            else if (N(f, "submit_ms") != 0) throw new Exception("Submit leaked outside running/draining");
            if (mode == "GameObjectCentralized" && N(f, "verified_entities") != 0) throw new Exception("Unexpected GO Entity count");
            if (f["stage"] == "IntervalRecovered" && (N(f, "verified_objects") != 0 || N(f, "verified_entities") != 0 || N(f, "pending_requests") != 0))
                throw new Exception("Natural drain did not finish");
        }
        if (!overlap || (!prepared && !grewPool)) throw new Exception("Overlap/pool growth was not exercised");
        foreach (var e in events)
        {
            if (!ids.Add(long.Parse(e["network_id"], CultureInfo.InvariantCulture)) || e["outcome"] != "Returned" ||
                N(e, "activated_frame") < N(e, "submitted_frame") || N(e, "returned_frame") < N(e, "activated_frame") ||
                N(e, "held_seconds") < .119999 || N(e, "request_to_active_ms") < 0 || N(e, "schedule_lateness_ms") < 0)
                throw new Exception("Invalid identity, observed latency or premature return");
        }
        foreach (var s in summary)
        {
            var raw = groups[s["case"] + ":" + s["stage"]];
            if (raw.Count != (int)N(s, "frames") || s["run_status"] != "Completed") throw new Exception("Summary frame/status mismatch");
            foreach (string metric in new[] { "main_thread_ms", "frame_interval_ms" })
            {
                var values = new List<double>(); foreach (var f in raw) values.Add(N(f, metric)); values.Sort();
                if (Math.Abs(Q(values, .99) - N(s, metric + "_p99")) > .00001) throw new Exception("p99 aggregate mismatch");
            }
            double gc = 0; foreach (var f in raw) gc += N(f, "gc_alloc_bytes");
            if (Math.Abs(gc - N(s, "gc_alloc_bytes_sum")) > .1) throw new Exception("GC sum mismatch");
        }
        foreach (var t in totals)
        {
            if (N(t, "submitted") != perRepeat || N(t, "activated") != perRepeat || N(t, "returned") != perRepeat ||
                N(t, "reused_after_return") <= 0 || N(t, "peak_active") <= 2 || N(t, "main_thread_ms_p99") <= 0)
                throw new Exception("Interval totals/latency counters incomplete");
        }
        string metadata = File.ReadAllText(Path.Combine(directory, "settings.csv"));
        if (!metadata.Contains("IntervalLifecycle") || !metadata.Contains("interval_spawn_seconds") || !metadata.Contains("interval_lifetime_seconds"))
            throw new Exception("Missing interval metadata");
    }

    private static void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Finish(false, message + "\n" + stack); }
    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
        Debug.Log($"[Interval CSV Validation] {(success ? "PASS" : "FAIL")}: {message}");
        EditorApplication.Exit(success ? 0 : 1);
    }
}
