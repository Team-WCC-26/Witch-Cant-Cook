using System.IO;
using Unity.Profiling;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Editor presentation only; built-in counters are reused without a runtime sampling manager.
[System.Serializable]
[ProfilerModuleMetadata("Ingredient Memory")]
public sealed class TEST_IngredientMemoryProfilerModule : ProfilerModule
{
    private static readonly ProfilerCounterDescriptor[] Counters =
    {
        new("GC Allocated In Frame", ProfilerCategory.Memory),
        new("GC Used Memory", ProfilerCategory.Memory),
        new(TEST_PerformanceSpawner.AllocatedMemoryCounterName, ProfilerCategory.Memory),
        new("Object Count", ProfilerCategory.Memory),
        new("Mesh Memory", ProfilerCategory.Memory)
    };

    public TEST_IngredientMemoryProfilerModule() : base(Counters, ProfilerModuleChartType.Line) { }
}

[CustomEditor(typeof(TEST_PerformanceSpawner))]
public sealed class TEST_PerformanceSpawnerEditor : Editor
{
    private static readonly int[] StressCounts = { 50, 100, 300, 500 };
    private static readonly int[] MemoryCounts = { 0, 50, 100, 200 };
    private TEST_PerformanceSpawner spawner;
    private TEST_PerformanceBootstrap bootstrap;
    private string lastStatus;
    private string lastBootstrapStatus;

    private void OnEnable()
    {
        spawner = (TEST_PerformanceSpawner)target;
        bootstrap = spawner.GetComponent<TEST_PerformanceBootstrap>();
        EditorApplication.update += RepaintOnStateChange;
    }

    private void OnDisable() => EditorApplication.update -= RepaintOnStateChange;

    private void RepaintOnStateChange()
    {
        if (!Application.isPlaying || spawner == null) return;
        string bootstrapStatus = bootstrap != null ? bootstrap.Status : null;
        if (lastStatus == spawner.Status && lastBootstrapStatus == bootstrapStatus) return;
        lastStatus = spawner.Status;
        lastBootstrapStatus = bootstrapStatus;
        Repaint();
    }

    public override void OnInspectorGUI()
    {
        using var automaticCapture = new EditorGUI.DisabledScope(spawner.GetComponent<TEST_ECSComparison>()?.IsCsvRunning == true);
        using (new EditorGUI.DisabledScope(Application.isPlaying && spawner.HasBatch))
            DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Floor: world-space line/grid. Conveyor: round-robin lanes; start distance/spacing along each path.\nOne batch at a time. Clear retains the pool. Restart Play or empty the pool for a cold spawn.", MessageType.Info);
        EditorGUILayout.LabelField("Memory test: zero > spawn > return > reuse", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Each step stays active until the next button. Enable Ingredient Memory in Profiler Modules to compare all five counters, or take a Memory Profiler snapshot after the state settles. Returning keeps the pool and loaded assets. No forced GC.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
            foreach (int count in MemoryCounts)
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying && !spawner.CanSpawn))
                    if (GUILayout.Button(Application.isPlaying ? (count == 0 ? "Zero (0)" : $"Spawn {count}") : $"{count}"))
                    {
                        if (!Application.isPlaying) Undo.RecordObject(spawner, "Set performance spawn count");
                        spawner.SpawnCount = count;
                        if (Application.isPlaying) spawner.SpawnConfigured();
                        else EditorUtility.SetDirty(spawner);
                    }
            }
        using (new EditorGUI.DisabledScope(!Application.isPlaying || !spawner.HasBatch))
            if (GUILayout.Button("Return all ingredients to pool")) spawner.ClearSpawned();
        using (new EditorGUI.DisabledScope(!spawner.CanRespawnReturned))
            if (GUILayout.Button("Respawn returned batch (same ingredient + count)")) spawner.RespawnReturnedBatch();
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("Spawn backend", spawner.UsesCentralized ? "Production GO queue (no ingredient Entities)" :
                spawner.UsesECS ? "Historical Hybrid ECS" : "Historical GameObject only (no default ECS World)");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Memory test stage", spawner.MemoryStage);
                EditorGUILayout.IntField("Requested active count", spawner.RequestedCount);
                EditorGUILayout.IntField("Verified active ingredients", spawner.VerifiedObjectCount);
                EditorGUILayout.IntField("Verified ECS entities", spawner.VerifiedEntityCount);
                EditorGUILayout.IntField("Last returned count", spawner.ReturnedCount);
                EditorGUILayout.IntField("Verified reused GameObjects", spawner.ReusedObjectCount);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime / spawn stress presets", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
            foreach (int count in StressCounts)
                using (new EditorGUI.DisabledScope(Application.isPlaying && !spawner.CanSpawn))
                    if (GUILayout.Button(Application.isPlaying ? $"Spawn {count}" : $"{count}"))
                    {
                        if (!Application.isPlaying) Undo.RecordObject(spawner, "Set performance spawn count");
                        spawner.SpawnCount = count;
                        if (Application.isPlaying) spawner.SpawnConfigured();
                        else EditorUtility.SetDirty(spawner);
                    }
        using (new EditorGUI.DisabledScope(!spawner.CanSpawn))
        {
            if (GUILayout.Button("Spawn configured count")) spawner.SpawnConfigured();
            if (GUILayout.Button("Prewarm pool (adds Prewarm Count)")) spawner.PrewarmPool();
            if (GUILayout.Button("Empty ingredient pool (cold next spawn)")) spawner.EmptyIngredientPool();
        }
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("State", spawner.Status);
            EditorGUILayout.LabelField("Verified GameObjects / Entities", $"{spawner.VerifiedObjectCount} / {spawner.VerifiedEntityCount}");
            if (!spawner.IsReady)
            {
                if (bootstrap != null) EditorGUILayout.HelpBox(bootstrap.Status, bootstrap.HasFailed ? MessageType.Error : MessageType.Info);
            }
        }
    }
}

/// <summary>Creates only test scene geometry and manager objects; never instantiates ingredient prefabs.</summary>
public static class TEST_PerformanceSceneEditor
{
    public const string Folder = "Assets/_Scripts/Tests/Performance";
    public const string ScenePath = Folder + "/PerformanceTestScene.unity";

    [MenuItem("Tools/Performance Tests/Open PerformanceTestScene")]
    public static void Open()
    {
        if (Application.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(ScenePath)) CreateScene();
        else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    [MenuItem("Tools/Performance Tests/Recreate PerformanceTestScene")]
    public static void Recreate()
    {
        if (Application.isPlaying || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Recreate performance scene", "Replace only PerformanceTestScene with its default test configuration?", "Recreate", "Cancel")) return;
        CreateScene();
    }

    // Command-line entry point. Does not change existing scenes or Build Settings.
    public static void CreateForBatch()
    {
        if (!File.Exists(ScenePath)) CreateScene();
        else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log("[PerformanceTest] Scene created/opened: " + ScenePath);
    }

    private static void CreateScene()
    {
        Directory.CreateDirectory(Folder);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Material floorMaterial = GetMaterial("PerformanceFloor", new Color(0.18f, 0.21f, 0.25f));
        Material beltMaterial = GetMaterial("PerformanceBelt", new Color(0.12f, 0.32f, 0.38f));

        var root = new GameObject("TEST_Performance");
        var bootstrap = root.AddComponent<TEST_PerformanceBootstrap>();
        var spawner = root.AddComponent<TEST_PerformanceSpawner>();
        var loop = root.AddComponent<TEST_PerformanceConveyorLoop>();
        DataManager data = AddSceneManager<DataManager>("DataManager (Resources JSON)");
        var dataSettings = new SerializedObject(data);
        dataSettings.FindProperty("useGSpread").boolValue = false;
        dataSettings.ApplyModifiedPropertiesWithoutUndo();
        ResourceManager resources = AddSceneManager<ResourceManager>("ResourceManager");
        ObjectPoolManager pool = AddSceneManager<ObjectPoolManager>("ObjectPoolManager");
        var router = new GameObject("ObjectNetworkRouter (disabled; local registration only)").AddComponent<ObjectNetworkRouter>();
        router.enabled = false;
        SetReferences(bootstrap, ("dataManager", data), ("resourceManager", resources), ("pool", pool), ("objectRouter", router));
        SetReferences(spawner, ("bootstrap", bootstrap));
        SetReferences(loop, ("spawner", spawner));

        MakeBox("Floor / Grid area", new Vector3(4f, -0.5f, 0f), new Vector3(140f, 1f, 150f), floorMaterial);
        var belts = new ConveyorBeltController[10];
        var beltRoot = new GameObject("TEST_Conveyors (10 lanes, 120m each)").transform;
        for (int i = 0; i < belts.Length; i++)
        {
            var lane = new GameObject($"Lane_{i + 1:00}");
            lane.transform.SetParent(beltRoot);
            float x = 10f + i * 5f;
            var path = lane.AddComponent<ConveyorPath>();
            var start = new GameObject("Start").transform;
            start.SetParent(lane.transform);
            start.position = new Vector3(x, 0.8f, -60f);
            var end = new GameObject("End (test loop; no server destroy trigger)").transform;
            end.SetParent(lane.transform);
            end.position = new Vector3(x, 0.8f, 60f);
            path.SetWaypoints(new[] { start, end });
            var belt = lane.AddComponent<ConveyorBeltController>();
            var settings = new SerializedObject(belt);
            settings.FindProperty("beltId").intValue = i + 1;
            settings.FindProperty("path").objectReferenceValue = path;
            settings.ApplyModifiedPropertiesWithoutUndo();
            MakeBox("Belt surface", new Vector3(x, 0.125f, 0f), new Vector3(2.5f, 0.25f, 122f), beltMaterial).transform.SetParent(lane.transform);
            belts[i] = belt;
        }
        var spawnSettings = new SerializedObject(spawner);
        SerializedProperty beltArray = spawnSettings.FindProperty("conveyorBelts");
        beltArray.arraySize = belts.Length;
        for (int i = 0; i < belts.Length; i++) beltArray.GetArrayElementAtIndex(i).objectReferenceValue = belts[i];
        spawnSettings.ApplyModifiedPropertiesWithoutUndo();

        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(4f, 125f, -115f);
        camera.transform.LookAt(new Vector3(4f, 0f, 0f));
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 500f;
        camera.fieldOfView = 60f;
        camera.backgroundColor = new Color(0.08f, 0.10f, 0.13f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.gameObject.AddComponent<AudioListener>();
        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.5f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light.shadows = LightShadows.Soft;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
    }

    private static T AddSceneManager<T>(string name) where T : MonoBehaviour
    {
        T manager = new GameObject(name).AddComponent<T>();
        var settings = new SerializedObject(manager);
        settings.FindProperty("isDestroyOnLoad").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return manager;
    }

    private static void SetReferences(Object target, params (string name, Object value)[] references)
    {
        var settings = new SerializedObject(target);
        foreach (var reference in references) settings.FindProperty(reference.name).objectReferenceValue = reference.value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject MakeBox(string name, Vector3 position, Vector3 scale, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = scale;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }

    private static Material GetMaterial(string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
