using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

/// <summary>Runs real captures and checks frame boundaries, pooled reuse, output and marker totals.</summary>
[InitializeOnLoad]
public static class TEST_ECSComparisonCsvValidation
{
    private const string Key = "TEST.Comparison.CsvValidation";
    private static double deadline;
    private static int pass;
    private static string firstDirectory;
    private static bool validatingBoth;
    static TEST_ECSComparisonCsvValidation()
    {
        if (!SessionState.GetBool(Key, false)) return;
        deadline = EditorApplication.timeSinceStartup + 180;
        validatingBoth = SessionState.GetBool(Key + ".Both", false);
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void RunBatchValidation()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity batch mode.");
        TEST_ECSComparisonSceneEditor.CreateForBatch();
        var target = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
        var settings = new SerializedObject(target);
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-ecsComparisonMode") settings.FindProperty("backend").enumValueIndex = (int)Enum.Parse(typeof(TEST_ECSComparison.Backend), args[i + 1]);
            if (args[i] == "-ecsPoolPreparation") settings.FindProperty("csvPoolPreparation").enumValueIndex = (int)Enum.Parse(typeof(TEST_ECSComparison.CsvPoolPreparation), args[i + 1]);
        }
        var counts = settings.FindProperty("csvCounts");
        counts.arraySize = 5;
        counts.GetArrayElementAtIndex(0).intValue = 0;
        counts.GetArrayElementAtIndex(1).intValue = 50;
        counts.GetArrayElementAtIndex(2).intValue = 100;
        counts.GetArrayElementAtIndex(3).intValue = 200;
        counts.GetArrayElementAtIndex(4).intValue = 500;
        settings.FindProperty("csvRepetitions").intValue = 2;
        settings.FindProperty("csvSampleFrames").intValue = 10;
        settings.FindProperty("csvSettleSeconds").floatValue = .05f;
        settings.FindProperty("csvStartOnReady").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        // Exercise real conveyor movement while capturing; do not save these validation settings.
        var spawner = new SerializedObject(target.GetComponent<TEST_PerformanceSpawner>());
        spawner.FindProperty("useConveyor").boolValue = true;
        spawner.ApplyModifiedPropertiesWithoutUndo();
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key + ".Both", false);
        deadline = EditorApplication.timeSinceStartup + 180;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.EnterPlaymode();
    }

    public static void RunBothBatchValidation()
        => RunDriverValidation(false);

    public static void RunAllBatchValidation()
        => RunDriverValidation(true);

    public static void RunBeforeAfterBatchValidation()
        => RunDriverValidation(false, true);

    private static void RunDriverValidation(bool all, bool beforeAfter = false)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity batch mode.");
        TEST_ECSComparisonSceneEditor.CreateForBatch();
        var comparison = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
        var settings = new SerializedObject(comparison);
        settings.FindProperty("csvCounts").arraySize = 1;
        settings.FindProperty("csvCounts").GetArrayElementAtIndex(0).intValue = 50;
        settings.FindProperty("csvRepetitions").intValue = 1;
        settings.FindProperty("backend").enumValueIndex = 0;
        settings.FindProperty("csvStartOnReady").boolValue = false;
        settings.FindProperty("csvSampleFrames").intValue = 10;
        settings.FindProperty("csvSettleSeconds").floatValue = .05f;
        settings.FindProperty("csvPoolPreparation").enumValueIndex = all ? 1 : 0;
        settings.ApplyModifiedPropertiesWithoutUndo();
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key + ".Both", true);
        SessionState.SetBool(Key + ".All", all);
        SessionState.SetBool(Key + ".BeforeAfter", beforeAfter);
        validatingBoth = true;
        deadline = EditorApplication.timeSinceStartup + 180;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        if (beforeAfter) TEST_ECSComparisonCsvRunner.RunBeforeAfter();
        else if (all) TEST_ECSComparisonCsvRunner.RunAll(); else TEST_ECSComparisonCsvRunner.RunBoth();
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("CSV validation timed out.");
            if (validatingBoth)
            {
                if (TEST_ECSComparisonCsvRunner.IsRunning) return;
                string folder = TEST_ECSComparisonCsvRunner.LastOutputDirectory;
                bool all = SessionState.GetBool(Key + ".All", false);
                bool beforeAfter = SessionState.GetBool(Key + ".BeforeAfter", false);
                foreach (string mode in beforeAfter ? new[] { "HybridECS", "GameObjectCentralized" } :
                    all ? new[] { "HybridECS", "GameObjectOnly", "HybridECSBatchedPooled" } : new[] { "HybridECS", "GameObjectOnly" })
                {
                    string[] data = File.ReadAllLines(Path.Combine(folder, mode, "summary.csv"));
                    if (data.Length != (all || beforeAfter ? 9 : 8) || !data[1].Contains(",Completed,")) throw new Exception("Multi-mode driver failed: " + mode);
                }
                var restored = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
                var state = new SerializedObject(restored);
                if (state.FindProperty("csvStartOnReady").boolValue || state.FindProperty("backend").enumValueIndex != 0)
                    throw new Exception("Editor settings were not restored after the two sessions.");
                FinishSuccess(all ? "three fresh Play sessions, all Prewarmed CSV folders and original Inspector settings restored" : "two fresh Play sessions, both CSV folders and original Inspector settings restored");
                return;
            }
            if (!EditorApplication.isPlaying) return;
            var target = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
            if (target == null) throw new Exception("Missing comparison component.");
            if (target.CaptureState == TEST_ECSComparison.CsvState.Failed) throw new Exception(target.CaptureStatus);
            if (pass >= 3)
            {
                if (pass == 3)
                {
                    if (!target.GetComponent<TEST_PerformanceSpawner>().CanSpawn) return;
                    target.BeginCsvCapture();
                    pass = 4;
                }
                else if (target.RecordedCsvFrames >= 5)
                {
                    target.CancelCsvCapture();
                    if (target.CaptureState != TEST_ECSComparison.CsvState.Cancelled ||
                        !File.ReadAllText(Path.Combine(target.LastCsvDirectory, "summary.csv")).Contains(",Cancelled,"))
                        throw new Exception("Cancellation did not save marked partial data.");
                    FinishSuccess(target.ActiveBackend + " zero/50/100/200/500 x 2 conveyor stages, CSV aggregates, no stale spawn marker, pooled reuse, unique repeated output and partial cancellation");
                }
                return;
            }
            if (target.CaptureState != TEST_ECSComparison.CsvState.Completed) return;
            VerifyFiles(target);
            if (pass == 0)
            {
                // A second capture must get a unique directory and start with a clean buffer.
                firstDirectory = target.LastCsvDirectory;
                target.GetComponent<TEST_PerformanceSpawner>().ClearSpawned();
                pass = 1;
            }
            if (pass == 1)
            {
                var spawner = target.GetComponent<TEST_PerformanceSpawner>();
                if (!spawner.CanSpawn) return;
                target.BeginCsvCapture();
                pass = 2;
                return;
            }
            if (target.LastCsvDirectory == firstDirectory) throw new Exception("Repeated capture overwrote the first output.");
            var current = target.GetComponent<TEST_PerformanceSpawner>();
            current.ClearSpawned();
            pass = 3;
        }
        catch (Exception exception) { FinishFailure(exception.ToString()); }
    }

    private static void VerifyFiles(TEST_ECSComparison target)
    {
        string[] lines = File.ReadAllLines(Path.Combine(target.LastCsvDirectory, "frames.csv"));
        string[] columns = lines[0].TrimStart('\uFEFF').Split(',');
        var indexes = new Dictionary<string, int>();
        for (int i = 0; i < columns.Length; i++) indexes.Add(columns[i], i);
        var stages = new HashSet<string>();
        var lastFrames = new HashSet<int>();
        double submit = 0, spawn = 0, gc = 0;
        bool memoryFound = false, cpuFound = false;
        int stableFrames = 0;
        bool prewarmed = new SerializedObject(target).FindProperty("csvPoolPreparation").enumValueIndex == 1;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] row = lines[i].Split(',');
            if (row.Length != columns.Length) throw new Exception("Malformed frame CSV row.");
            string stage = row[indexes["stage"]];
            int count = int.Parse(row[indexes["count"]], CultureInfo.InvariantCulture);
            int frame = int.Parse(row[indexes["frame"]], CultureInfo.InvariantCulture);
            if (!lastFrames.Add(frame)) throw new Exception("A frame was sampled twice.");
            stages.Add(row[indexes["case"]] + ":" + stage);
            string submitValue = row[indexes["submit_ms"]];
            double request = Number(submitValue);
            double process = Number(row[indexes[target.UsesCentralized ? "central_spawn_ms" : target.UsesECS ? "spawn_process_ms" : "go_spawn_ms"]]);
            if (prewarmed && (stage == "FirstSpawn" || stage == "ReuseSpawn") && Number(row[indexes["pool_create_ms"]]) != 0)
                throw new Exception("A prewarmed spawn instantiated another GameObject.");
            if (stage == "FirstSpawn" || stage == "ReuseSpawn") { submit += request; spawn += process; }
            else if (request != 0 || (!target.UsesECS && process != 0)) throw new Exception("A completed spawn marker leaked into a later stage.");
            if (stage == "Spawned" || stage == "Reused")
            {
                if (int.Parse(row[indexes["verified_objects"]]) != count || int.Parse(row[indexes["verified_entities"]]) != (target.UsesECS ? count : 0))
                    throw new Exception("Recorded ingredient/entity counts do not match stage.");
                if (stage == "Reused" && int.Parse(row[indexes["reused_objects"]]) != count) throw new Exception("Pool reuse was not verified.");
                if (target.UsesPairedPooling && int.Parse(row[indexes["paired_entity_capacity"]]) != count)
                    throw new Exception("Recorded paired Entity capacity mismatch.");
            }
            if (target.UsesPairedPooling && (stage == "ReuseSpawn" || (prewarmed && stage == "FirstSpawn")) && Number(row[indexes["pooled_entity_create_ms"]]) != 0)
                throw new Exception("A prepared/reused spawn created another Entity.");
            if (stage == "Baseline" || stage == "Spawned" || stage == "Returned" || stage == "Reused") stableFrames++;
            gc += Number(row[indexes["gc_alloc_bytes"]]);
            memoryFound |= Number(row[indexes["total_allocated_bytes"]]) > 0;
            cpuFound |= Number(row[indexes["main_thread_ms"]]) > 0;
        }
        if (stages.Count != (prewarmed ? 80 : 70) || stableFrames != 400 || submit <= 0 || spawn <= 0 || !memoryFound || !cpuFound)
            throw new Exception($"Missing CSV stage/counter: stages={stages.Count}, stable={stableFrames}, submit={submit}, spawn={spawn}, memory={memoryFound}, cpu={cpuFound}");
        string[] summary = File.ReadAllLines(Path.Combine(target.LastCsvDirectory, "summary.csv"));
        var headers = summary[0].TrimStart('\uFEFF').Split(',');
        int sumIndex = Array.IndexOf(headers, "gc_alloc_bytes_sum");
        double summaryGc = 0;
        for (int i = 1; i < summary.Length; i++) summaryGc += Number(summary[i].Split(',')[sumIndex]);
        if (summary.Length != (prewarmed ? 81 : 71) || Math.Abs(summaryGc - gc) > .1) throw new Exception("Summary stage count/GC aggregate mismatch.");
        if (!target.UsesECS && !target.UsesCentralized && World.DefaultGameObjectInjectionWorld != null) throw new Exception("CSV recreated an ECS World in historical GO mode.");
        if (target.UsesCentralized)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || world.GetExistingSystemManaged<IngredientSpawnSystem>() != null)
                throw new Exception("Centralized comparison did not preserve production World configuration.");
        }
        if (!File.Exists(Path.Combine(target.LastCsvDirectory, "settings.csv"))) throw new Exception("Missing metadata.");
        string metadata = File.ReadAllText(Path.Combine(target.LastCsvDirectory, "settings.csv"));
        if (!metadata.Contains(ObjectPoolManager.SpawnPreparationVersion) || !metadata.Contains(prewarmed ? "Prewarmed" : "Cold"))
            throw new Exception("Missing preparation version/condition metadata.");
    }

    private static double Number(string value) => string.IsNullOrEmpty(value) ? 0 : double.Parse(value, CultureInfo.InvariantCulture);
    private static void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) FinishFailure(message + "\n" + stack); }
    private static void FinishFailure(string reason)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        Debug.LogError("[ECSComparison CSV Validation] FAIL: " + reason);
        EditorApplication.Exit(1);
    }

    private static void FinishSuccess(string message)
    {
        SessionState.SetBool(Key, false);
        SessionState.SetBool(Key + ".Both", false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        Debug.Log("[ECSComparison CSV Validation] PASS: " + message);
        EditorApplication.Exit(0);
    }
}
