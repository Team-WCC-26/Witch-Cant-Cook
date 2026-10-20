using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(TEST_ECSComparison))]
public sealed class TEST_ECSComparisonEditor : Editor
{
    private double nextIntervalRepaint;
    private void OnEnable() => EditorApplication.update += RefreshIntervalStatus;
    private void OnDisable() => EditorApplication.update -= RefreshIntervalStatus;
    private void RefreshIntervalStatus()
    {
        if (!Application.isPlaying || target == null || EditorApplication.timeSinceStartup < nextIntervalRepaint) return;
        var comparison = (TEST_ECSComparison)target;
        if (!comparison.IsCsvRunning || !comparison.IsIntervalScenario) return;
        nextIntervalRepaint = EditorApplication.timeSinceStartup + .5;
        Repaint();
    }
    public override void OnInspectorGUI()
    {
        using (new EditorGUI.DisabledScope(Application.isPlaying)) DrawDefaultInspector();
        EditorGUILayout.HelpBox("GameObjectCentralized runs the current production GO queue. HybridECS and HybridECSBatchedPooled are historical test-only baselines; GameObjectOnly is the original GO comparison. Restart Play to switch.", MessageType.Info);
        var comparison = (TEST_ECSComparison)target;
        EditorGUILayout.HelpBox("CSV Pool Preparation: Cold matches earlier runs. Prewarmed prepares the case quantity in a separate Prewarm stage before Baseline. Both then run FirstSpawn > Spawned > Return > Returned > ReuseSpawn > Reused. Auto Spawn must be off; the spawner's manual Prewarm Count is ignored by CSV.", MessageType.Info);
        if (!Application.isPlaying)
        {
            using (new EditorGUI.DisabledScope(TEST_ECSComparisonCsvRunner.IsRunning))
            {
                if (GUILayout.Button("Run BEFORE / AFTER (Prewarmed) and save CSV")) TEST_ECSComparisonCsvRunner.RunBeforeAfter();
                if (GUILayout.Button("Run INTERVAL BEFORE / AFTER (Prewarmed) and save CSV")) TEST_ECSComparisonCsvRunner.RunIntervalBeforeAfter();
                if (GUILayout.Button("Run BOTH modes and save CSV")) TEST_ECSComparisonCsvRunner.RunBoth();
                if (GUILayout.Button("Run ALL THREE modes and save CSV")) TEST_ECSComparisonCsvRunner.RunAll();
            }
            if (!string.IsNullOrEmpty(TEST_ECSComparisonCsvRunner.LastOutputDirectory) && GUILayout.Button("Open last CSV folder"))
                TEST_ECSComparisonCsvRunner.OpenLastOutput();
        }
        else
        {
            EditorGUILayout.LabelField("CSV state", comparison.CaptureState.ToString());
            EditorGUILayout.LabelField("CSV status", comparison.CaptureStatus);
            EditorGUILayout.LabelField("CSV frames", comparison.RecordedCsvFrames.ToString());
            using (new EditorGUI.DisabledScope(comparison.IsCsvRunning))
                if (GUILayout.Button("Run this mode and save CSV")) comparison.BeginCsvCapture();
            using (new EditorGUI.DisabledScope(!comparison.IsCsvRunning))
                if (GUILayout.Button("Stop CSV (save partial result)")) comparison.CancelCsvCapture();
            if (!string.IsNullOrEmpty(comparison.LastCsvDirectory) && GUILayout.Button("Open CSV folder"))
                EditorUtility.RevealInFinder(comparison.LastCsvDirectory);
        }
        if (comparison.IsIntervalScenario)
        {
            EditorGUILayout.HelpBox("Interval: spawn a small burst every Interval Spawn Seconds; return each ingredient after Interval Lifetime Seconds from observed activation. Bursts overlap. CSV Repetitions applies; batch Counts/Sample Frames do not. The interval comparison button runs old ECS and current GO in fresh sessions. No server is created.", MessageType.Info);
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Interval elapsed (s)", comparison.IntervalElapsedSeconds.ToString("F1"));
                EditorGUILayout.LabelField("Bursts submitted / planned", $"{comparison.IntervalCompletedBursts} / {comparison.IntervalPlannedBursts}");
                EditorGUILayout.LabelField("Active / pending", $"{comparison.IntervalLiveCount} / {comparison.IntervalPending}");
                EditorGUILayout.LabelField("Submitted / returned / reused", $"{comparison.IntervalSubmitted} / {comparison.IntervalReturned} / {comparison.IntervalReused}");
            }
        }
        if (!Application.isPlaying) return;
        EditorGUILayout.LabelField("Locked backend", comparison.ActiveBackend.ToString());
        if (comparison.UsesPairedPooling)
        {
            var spawner = comparison.GetComponent<TEST_PerformanceSpawner>();
            EditorGUILayout.LabelField("Paired Entity capacity (active + inactive)", spawner.PairedEntityCapacity.ToString());
            EditorGUILayout.LabelField("Paired Entities created / reused (World total)", $"{spawner.PairedEntitiesCreated} / {spawner.PairedEntitiesReused}");
        }
        if (comparison.InitializationError != null) EditorGUILayout.HelpBox(comparison.InitializationError, MessageType.Error);
    }
}

public static class TEST_ECSComparisonSceneEditor
{
    public const string Folder = "Assets/_Scripts/Tests/ECSComparison";
    public const string ScenePath = Folder + "/ECSComparisonTestScene.unity";

    [MenuItem("Tools/Performance Tests/Open ECSComparisonTestScene")]
    public static void Open()
    {
        if (Application.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreateForBatch();
    }

    [MenuItem("Tools/Performance Tests/Recreate ECSComparisonTestScene")]
    public static void Recreate()
    {
        if (Application.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!EditorUtility.DisplayDialog("Recreate comparison scene", "Replace only ECSComparisonTestScene using the existing performance scene's geometry?", "Recreate", "Cancel")) return;
        CreateScene();
    }

    public static void CreateForBatch()
    {
        if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        else CreateScene();
    }

    private static void CreateScene()
    {
        Directory.CreateDirectory(Folder);
        // Load/copy in memory. Never save back to the existing performance scene or Build Settings.
        TEST_PerformanceSceneEditor.CreateForBatch();
        var spawner = Object.FindFirstObjectByType<TEST_PerformanceSpawner>();
        spawner.gameObject.name = "TEST_ECSComparison";
        spawner.gameObject.AddComponent<TEST_ECSComparison>();
        var settings = new SerializedObject(spawner);
        settings.FindProperty("autoSpawn").boolValue = false;
        settings.FindProperty("spawnCount").intValue = 50;
        settings.FindProperty("prewarmCount").intValue = 0;
        settings.FindProperty("useConveyor").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = spawner.gameObject;
    }
}
