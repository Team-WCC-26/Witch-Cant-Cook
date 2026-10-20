using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch-mode integration check against the real pool, spawn system, conveyor and ECS cleanup.</summary>
[InitializeOnLoad]
public static class TEST_PerformanceValidation
{
    private const string RunningKey = "TEST.PerformanceValidation.Running";
    private static readonly int[] Counts = { 50, 100, 300, 500 };
    private static readonly int[] MemoryCounts = { 0, 50, 100, 200, 50 };
    private static readonly HashSet<int> memoryObjectIds = new();
    private static readonly HashSet<long> memoryNetworkIds = new();
    private static TEST_PerformanceSpawner spawner;
    private static TEST_PerformanceBootstrap bootstrap;
    private static int phase;
    private static int caseIndex;
    private static int resumeFrame;
    private static double deadline;
    private static ConveyorItemState[] previousStates;
    private static int memoryCase;
    private static bool prewarmValidated;

    static TEST_PerformanceValidation()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        deadline = EditorApplication.timeSinceStartup + 180;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    // Invoke without -quit: this method exits Unity after validation has completed.
    public static void RunBatchValidation()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the batch-mode command line for this validation.");
        TEST_PerformanceSceneEditor.CreateForBatch();
        var target = UnityEngine.Object.FindFirstObjectByType<TEST_PerformanceSpawner>();
        var settings = new SerializedObject(target);
        settings.FindProperty("autoSpawn").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        // Changes to auto-spawn are in memory only; the delivered scene keeps its default setting.
        SessionState.SetBool(RunningKey, true);
        deadline = EditorApplication.timeSinceStartup + 180;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.timeSinceStartup > deadline) { Finish(false, "Validation timed out"); return; }
        try
        {
            if (spawner == null) spawner = UnityEngine.Object.FindFirstObjectByType<TEST_PerformanceSpawner>();
            if (bootstrap == null) bootstrap = UnityEngine.Object.FindFirstObjectByType<TEST_PerformanceBootstrap>();
            if (bootstrap == null || spawner == null) throw new Exception("Test components are missing");
            if (bootstrap.HasFailed) throw new Exception(bootstrap.Status);
            if (!spawner.IsReady || Time.frameCount <= resumeFrame) return;
            if (!prewarmValidated)
            {
                // Exercise a never-spawned key, before EmptyIngredientPool/Pop can create its queue.
                ObjectPoolManager pool = ObjectPoolManager.Instance;
                string key = DataManager.Instance.GetIngredient().Get(spawner.IngredientId).prefabName;
                if (pool.GetPooledCount(key) != 0) throw new Exception("Expected empty startup pool");
                pool.PrewarmPool(key, 2);
                if (pool.GetPooledCount(key) != 2) throw new Exception("Fresh-key prewarm discarded instances");
                GameObject go = pool.Pop(key, Vector3.zero, Quaternion.identity, out CatchableObj food, out IngredientReaction reaction);
                if (food != go.GetComponent<CatchableObj>() || reaction != go.GetComponent<IngredientReaction>())
                    throw new Exception("Cached spawn components mismatch");
                var identity = go.AddComponent<FoodRecipeIdentity>();
                var identitySettings = new SerializedObject(identity);
                identitySettings.FindProperty("recipeId").intValue = 123;
                identitySettings.ApplyModifiedPropertiesWithoutUndo();
                pool.Push(go);
                if (identity.RecipeId != 0 || pool.GetPooledCount(key) != 2 || go.activeSelf)
                    throw new Exception("Runtime-added IPoolable was not reset/returned");
                pool.ClearPool(key);
                prewarmValidated = true;
                resumeFrame = Time.frameCount + 1;
                return;
            }
            if (spawner.MemoryStage == TEST_PerformanceSpawner.MemoryTestStage.Failed)
                throw new Exception(spawner.Status);
            if (caseIndex >= 16) { TickMemoryCases(); return; }
            if (phase == 0)
            {
                int count = Counts[caseIndex % 4];
                bool conveyor = caseIndex >= 8;
                bool warm = caseIndex % 8 >= 4;
                if (!spawner.CanSpawn) return;
                var settings = new SerializedObject(spawner);
                settings.FindProperty("useConveyor").boolValue = conveyor;
                settings.FindProperty("prewarmCount").intValue = count;
                settings.ApplyModifiedPropertiesWithoutUndo();
                spawner.SpawnCount = count;
                spawner.EmptyIngredientPool();
                if (warm && !spawner.PrewarmPool()) throw new Exception("Prewarm failed");
                resumeFrame = Time.frameCount + 1;
                phase = 1;
            }
            else if (phase == 1)
            {
                spawner.SpawnConfigured();
                phase = 2;
                deadline = EditorApplication.timeSinceStartup + 30;
            }
            else if (phase == 2)
            {
                if (spawner.VerifiedObjectCount != spawner.SpawnCount) return;
                VerifyCounts(spawner.SpawnCount);
                if (spawner.UseConveyor)
                {
                    previousStates = new ConveyorItemState[spawner.Items.Count];
                    for (int i = 0; i < spawner.Items.Count; i++)
                    {
                        var item = spawner.Items[i];
                        previousStates[i] = item.BeltState;
                        // Force one boundary crossing without changing production belt speed or waiting a minute.
                        float initialDistance = (Time.time - item.BeltState.LocalSpawnTime) * item.Belt.Speed;
                        item.BeltState.LocalSpawnTime = Time.time - (item.Belt.Path.TotalLength + initialDistance) / item.Belt.Speed;
                    }
                    resumeFrame = Time.frameCount + 2;
                    phase = 3;
                }
                else Clear();
            }
            else if (phase == 3)
            {
                for (int i = 0; i < spawner.Items.Count; i++)
                {
                    var item = spawner.Items[i];
                    if (ReferenceEquals(previousStates[i], item.BeltState) || !item.Belt.IsRegistered(item.NetworkId) ||
                        item.GameObject.GetComponent<CatchableObj>().NetworkId != item.NetworkId)
                        throw new Exception("Conveyor loop did not preserve registered object identity");
                }
                VerifyCounts(spawner.SpawnCount);
                Clear();
            }
            else if (phase == 4 && spawner.CanSpawn)
            {
                VerifyCounts(0);
                Debug.Log($"[PerformanceTest Validation] PASS case {caseIndex + 1}/16");
                caseIndex++;
                phase = 0;
            }
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }

    private static void TickMemoryCases()
    {
        if (memoryCase == MemoryCounts.Length)
        {
            Finish(true, "16 stress batches + 5 memory scenarios passed: zero, 50/100/200 return/reuse, FIFO reuse, and five Profiler counters.");
            return;
        }
        int count = MemoryCounts[memoryCase];
        if (phase == 0)
        {
            if (!spawner.CanSpawn) return;
            var settings = new SerializedObject(spawner);
            settings.FindProperty("useConveyor").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            spawner.SpawnCount = count;
            // Keep the 200-object pool for the final 50-object FIFO reuse scenario.
            if (memoryCase != 4) spawner.EmptyIngredientPool();
            resumeFrame = Time.frameCount + 2;
            phase = 10;
        }
        else if (phase == 10)
        {
            spawner.SpawnConfigured();
            if (count == 0)
            {
                VerifyCounts(0);
                if (spawner.HasBatch || spawner.MemoryStage != TEST_PerformanceSpawner.MemoryTestStage.Zero)
                    throw new Exception("Zero preset submitted a batch");
                VerifyMemoryCounters();
                memoryCase++;
                phase = 0;
            }
            else phase = 11;
        }
        else if (phase == 11)
        {
            if (spawner.MemoryStage != TEST_PerformanceSpawner.MemoryTestStage.Spawned) return;
            VerifyCounts(count);
            memoryObjectIds.Clear();
            memoryNetworkIds.Clear();
            foreach (var item in spawner.Items)
            {
                memoryObjectIds.Add(item.GameObject.GetInstanceID());
                memoryNetworkIds.Add(item.NetworkId);
            }
            spawner.ClearSpawned();
            if (spawner.CanRespawnReturned) throw new Exception("Reuse was available before ECS cleanup");
            phase = 12;
            resumeFrame = Time.frameCount + 2;
        }
        else if (phase == 12)
        {
            if (!spawner.CanSpawn) return;
            VerifyCounts(0);
            if (spawner.MemoryStage != TEST_PerformanceSpawner.MemoryTestStage.Returned ||
                spawner.ReturnedCount != count || !spawner.CanRespawnReturned || spawner.Items.Count != 0)
                throw new Exception("Return stage/count mismatch");
            // Every test-owned object must still exist in the pool, now inactive.
            int pooledCount = 0;
            foreach (var food in UnityEngine.Object.FindObjectsByType<CatchableObj>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (memoryObjectIds.Contains(food.gameObject.GetInstanceID()) && !food.gameObject.activeSelf) pooledCount++;
            if (pooledCount != count) throw new Exception("Returned objects were destroyed or remained active");
            spawner.RespawnReturnedBatch();
            phase = 13;
        }
        else if (phase == 13)
        {
            if (spawner.MemoryStage != TEST_PerformanceSpawner.MemoryTestStage.Reused) return;
            VerifyCounts(count);
            if (spawner.ReusedObjectCount != count || spawner.SpawnCount != count)
                throw new Exception("Reused object/count verification failed");
            foreach (var item in spawner.Items)
            {
                if (memoryNetworkIds.Contains(item.NetworkId)) throw new Exception("Reuse recycled an old NetworkID");
                if (memoryCase != 4 && !memoryObjectIds.Contains(item.GameObject.GetInstanceID()))
                    throw new Exception("Reuse instantiated a replacement GameObject");
            }
            VerifyMemoryCounters();
            spawner.ClearSpawned();
            phase = 14;
            resumeFrame = Time.frameCount + 2;
        }
        else if (phase == 14 && spawner.CanSpawn)
        {
            VerifyCounts(0);
            Debug.Log($"[PerformanceTest Validation] PASS memory case {memoryCase + 1}/5 ({count} ingredients)");
            memoryCase++;
            phase = 0;
        }
    }

    private static void VerifyMemoryCounters()
    {
        string[] names = { "GC Allocated In Frame", "GC Used Memory", TEST_PerformanceSpawner.AllocatedMemoryCounterName, "Object Count", "Mesh Memory" };
        foreach (string name in names)
        {
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, name);
            if (!recorder.Valid) throw new Exception("Profiler counter unavailable: " + name);
        }
    }

    private static void Clear()
    {
        spawner.ClearSpawned();
        phase = 4;
        resumeFrame = Time.frameCount + 1;
    }

    private static void VerifyCounts(int expected)
    {
        EntityManager manager = World.DefaultGameObjectInjectionWorld.EntityManager;
        using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<IngredientInfo>(), ComponentType.ReadOnly<NetworkID>());
        using var requests = manager.CreateEntityQuery(ComponentType.ReadOnly<IngredientSpawnRequest>());
        if (query.CalculateEntityCount() != (spawner.UsesECS ? expected : 0) || !requests.IsEmptyIgnoreFilter || spawner.PendingCentralRequests != 0 ||
            ObjectPoolManager.Instance.activeObjDict.Count != expected || bootstrap.Router.catchableDics.Count != expected)
            throw new Exception($"Object/router/request/ECS count mismatch; expected {expected}");
        using var ids = query.ToComponentDataArray<NetworkID>(Allocator.Temp);
        foreach (NetworkID id in ids)
            if (id.Value < TEST_PerformanceSpawner.FirstTestNetworkId) throw new Exception("Test ID policy violated");
        if (UnityEngine.Object.FindAnyObjectByType<Server.ServerManager>() != null) throw new Exception("Unexpected ServerManager");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        foreach (var debug in UnityEngine.Object.FindObjectsByType<ConveyorSpawnTimingDebug>(FindObjectsSortMode.None))
            if (debug.enabled) throw new Exception("Timing debugger must be disabled");
#endif
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            Finish(false, message + "\n" + stack);
    }

    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(RunningKey, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        Debug.Log($"[PerformanceTest Validation] {(success ? "PASS" : "FAIL")}: {message}");
        EditorApplication.Exit(success ? 0 : 1);
    }
}
