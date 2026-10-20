using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Two fresh Play sessions; never switches backend in an existing World.</summary>
[InitializeOnLoad]
public static class TEST_ECSComparisonCsvRunner
{
    private const string Key = "TEST.Comparison.CsvRunner.";
    public static bool IsRunning => SessionState.GetBool(Key + "Running", false);
    public static string LastOutputDirectory => SessionState.GetString(Key + "Output", "");
    private static double deadline;
    private static TEST_ECSComparison activeComparison;

    static TEST_ECSComparisonCsvRunner()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        deadline = EditorApplication.timeSinceStartup + 600;
        if (IsRunning && double.TryParse(SessionState.GetString(Key + "Deadline", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double savedDeadline))
            deadline = savedDeadline;
    }

    [MenuItem("Tools/Performance Tests/Run BOTH comparison modes to CSV")]
    public static void RunBoth() => RunModes(2);

    [MenuItem("Tools/Performance Tests/Run ALL THREE comparison modes to CSV")]
    public static void RunAll() => RunModes(3);

    [MenuItem("Tools/Performance Tests/Run BEFORE-AFTER Prewarmed to CSV")]
    public static void RunBeforeAfter() => RunModes(2, true);

    [MenuItem("Tools/Performance Tests/Run INTERVAL BEFORE-AFTER Prewarmed to CSV")]
    public static void RunIntervalBeforeAfter() => RunModes(2, true, true);

    private static void RunModes(int modeCount, bool beforeAfter = false, bool interval = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || IsRunning) return;
        var comparison = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
        if (comparison == null || comparison.gameObject.scene.name != TEST_ECSComparison.SceneName ||
            UnityEngine.SceneManagement.SceneManager.sceneCount != 1)
        { Debug.LogError("Open ECSComparisonTestScene alone before starting the CSV test."); return; }
        var spawner = comparison.GetComponent<TEST_PerformanceSpawner>();
        if (spawner == null || spawner.AutoSpawn)
        { Debug.LogError("Disable Auto Spawn before starting the CSV test."); return; }
        SessionState.SetString(Key + "Original", EditorJsonUtility.ToJson(comparison));
        SessionState.SetBool(Key + "BeforeAfter", beforeAfter);
        if (beforeAfter)
        {
            var settings = new SerializedObject(comparison);
            settings.FindProperty("csvPoolPreparation").enumValueIndex = (int)TEST_ECSComparison.CsvPoolPreparation.Prewarmed;
            settings.FindProperty("csvScenario").enumValueIndex = (int)(interval ? TEST_ECSComparison.CsvScenario.IntervalLifecycle : TEST_ECSComparison.CsvScenario.BatchLifecycle);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
        SessionState.SetString(Key + "Group", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        SessionState.SetInt(Key + "Mode", 0);
        SessionState.SetInt(Key + "ModeCount", modeCount);
        SessionState.SetInt(Key + "Phase", 0);
        SessionState.SetBool(Key + "Running", true);
        BeginSession(comparison);
    }

    private static void BeginSession(TEST_ECSComparison comparison)
    {
        int mode = SessionState.GetInt(Key + "Mode", 0);
        var backend = SessionState.GetBool(Key + "BeforeAfter", false) && mode == 1
            ? TEST_ECSComparison.Backend.GameObjectCentralized : (TEST_ECSComparison.Backend)mode;
        comparison.ConfigureAutomaticCsv(backend, true, SessionState.GetString(Key + "Group", ""));
        SessionState.SetInt(Key + "Phase", 0);
        activeComparison = null;
        deadline = EditorApplication.timeSinceStartup + (comparison.IsIntervalScenario ? comparison.EstimatedIntervalCsvSeconds : 600);
        SessionState.SetString(Key + "Deadline", deadline.ToString("R", CultureInfo.InvariantCulture));
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Tools/Performance Tests/Open last comparison CSV folder")]
    public static void OpenLastOutput()
    {
        if (!string.IsNullOrEmpty(LastOutputDirectory) && System.IO.Directory.Exists(LastOutputDirectory))
            EditorUtility.RevealInFinder(LastOutputDirectory);
    }

    private static void Tick()
    {
        if (!IsRunning || !EditorApplication.isPlaying || SessionState.GetInt(Key + "Phase", 0) != 0) return;
        if (activeComparison == null) activeComparison = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
        var comparison = activeComparison;
        if (comparison == null) { Stop("Comparison component disappeared."); return; }
        if (EditorApplication.timeSinceStartup > deadline) { Stop("CSV run timed out."); return; }
        if (comparison.CaptureState == TEST_ECSComparison.CsvState.Completed)
        {
            Debug.Log("[ECSComparison CSV] Saved " + comparison.LastCsvDirectory);
            SessionState.SetString(Key + "Output", System.IO.Path.GetDirectoryName(comparison.LastCsvDirectory));
            SessionState.SetInt(Key + "Phase", 1);
            EditorApplication.ExitPlaymode();
        }
        else if (comparison.CaptureState == TEST_ECSComparison.CsvState.Cancelled || comparison.CaptureState == TEST_ECSComparison.CsvState.Failed)
            Stop(comparison.CaptureStatus);
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (!IsRunning || state != PlayModeStateChange.EnteredEditMode) return;
        int mode = SessionState.GetInt(Key + "Mode", 0);
        int modeCount = SessionState.GetInt(Key + "ModeCount", 2);
        if (SessionState.GetInt(Key + "Phase", 0) == 1 && mode + 1 < modeCount)
        {
            SessionState.SetInt(Key + "Mode", mode + 1);
            EditorApplication.delayCall += () =>
            {
                if (!IsRunning) return;
                var comparison = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
                if (comparison != null) BeginSession(comparison); else Restore();
            };
        }
        else
        {
            string output = SessionState.GetString(Key + "Output", "");
            bool completed = SessionState.GetInt(Key + "Phase", 0) == 1;
            Restore();
            if (completed) Debug.Log($"[ECSComparison CSV] {modeCount} modes completed. CSV folder: " + output);
        }
    }

    private static void Stop(string reason)
    {
        Debug.LogWarning("[ECSComparison CSV] Automatic run stopped: " + reason);
        SessionState.SetInt(Key + "Phase", 2);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); else Restore();
    }

    private static void Restore()
    {
        var comparison = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
        string original = SessionState.GetString(Key + "Original", "");
        if (comparison != null && !string.IsNullOrEmpty(original)) EditorJsonUtility.FromJsonOverwrite(original, comparison);
        activeComparison = null;
        SessionState.SetBool(Key + "Running", false);
    }
}
