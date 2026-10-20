using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

/// <summary>Separate Unity invocation per backend: zero/spawn/return/reuse, floor and conveyor.</summary>
[InitializeOnLoad]
public static class TEST_ECSComparisonValidation
{
    private const string RunningKey = "TEST.ECSComparisonValidation.Running";
    private static readonly int[] Counts = { 0, 50, 100, 200, 500 };
    private static readonly HashSet<int> originalObjects = new();
    private static readonly HashSet<long> originalNetworkIds = new();
    private static readonly Dictionary<int, Entity> originalEntities = new();
    private static TEST_ECSComparison comparison;
    private static TEST_PerformanceSpawner spawner;
    private static TEST_PerformanceBootstrap bootstrap;
    private static int caseIndex;
    private static int phase;
    private static int resumeFrame;
    private static double deadline;
    private static ConveyorItemState[] previousBeltStates;

    static TEST_ECSComparisonValidation()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        deadline = EditorApplication.timeSinceStartup + 180;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void RunBatchValidation()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this validation from Unity batch mode, not a live editing session.");
        TEST_ECSComparisonSceneEditor.CreateForBatch();
        TEST_ECSComparison.Backend backend = TEST_ECSComparison.Backend.HybridECS;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-ecsComparisonMode") backend = (TEST_ECSComparison.Backend)Enum.Parse(typeof(TEST_ECSComparison.Backend), args[i + 1]);
        var target = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
        var settings = new SerializedObject(target);
        settings.FindProperty("backend").enumValueIndex = (int)backend;
        settings.ApplyModifiedPropertiesWithoutUndo();
        // Set the mode only in memory. The delivered scene remains HybridECS by default.
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
            if (comparison == null) comparison = UnityEngine.Object.FindFirstObjectByType<TEST_ECSComparison>();
            if (spawner == null) spawner = UnityEngine.Object.FindFirstObjectByType<TEST_PerformanceSpawner>();
            if (bootstrap == null) bootstrap = UnityEngine.Object.FindFirstObjectByType<TEST_PerformanceBootstrap>();
            if (comparison == null || spawner == null || bootstrap == null) throw new Exception("Comparison components missing");
            if (comparison.InitializationError != null || bootstrap.HasFailed || spawner.MemoryStage == TEST_PerformanceSpawner.MemoryTestStage.Failed)
                throw new Exception(comparison.InitializationError ?? (bootstrap.HasFailed ? bootstrap.Status : spawner.Status));
            if (!spawner.IsReady || Time.frameCount <= resumeFrame) return;
            bool conveyor = caseIndex >= Counts.Length;
            if (caseIndex == 10) { Finish(true, $"{comparison.ActiveBackend}: 10 floor/conveyor cases passed (0/50/100/200/500, pool reuse, new IDs, ECS cleanup/absence, conveyor loop)"); return; }
            int count = Counts[caseIndex % Counts.Length];
            if (phase == 0)
            {
                if (!spawner.CanSpawn) return;
                var settings = new SerializedObject(spawner);
                settings.FindProperty("useConveyor").boolValue = conveyor;
                settings.ApplyModifiedPropertiesWithoutUndo();
                spawner.SpawnCount = count;
                spawner.EmptyIngredientPool();
                VerifyCounts(0);
                phase = 1;
                resumeFrame = Time.frameCount + 2;
                deadline = EditorApplication.timeSinceStartup + 30;
            }
            else if (phase == 1)
            {
                spawner.SpawnConfigured();
                if (count == 0)
                {
                    VerifyCounts(0);
                    if (spawner.HasBatch) throw new Exception("Zero preset created a batch");
                    CompleteCase();
                }
                else phase = 2;
            }
            else if (phase == 2)
            {
                if (spawner.MemoryStage != TEST_PerformanceSpawner.MemoryTestStage.Spawned) return;
                VerifyCounts(count);
                originalObjects.Clear();
                originalNetworkIds.Clear();
                originalEntities.Clear();
                foreach (var item in spawner.Items)
                {
                    originalObjects.Add(item.GameObject.GetInstanceID());
                    originalNetworkIds.Add(item.NetworkId);
                    if (comparison.UsesPairedPooling)
                    {
                        var system = IngredientEntityLifecycle.PooledSpawnSystem;
                        if (!system.TryGetPairedEntity(item.GameObject, out Entity entity)) throw new Exception("Missing paired Entity");
                        originalEntities.Add(item.GameObject.GetInstanceID(), entity);
                    }
                }
                if (conveyor)
                {
                    previousBeltStates = new ConveyorItemState[count];
                    for (int i = 0; i < count; i++)
                    {
                        var item = spawner.Items[i];
                        previousBeltStates[i] = item.BeltState;
                        float distance = (Time.time - item.BeltState.LocalSpawnTime) * item.Belt.Speed;
                        item.BeltState.LocalSpawnTime = Time.time - (item.Belt.Path.TotalLength + distance) / item.Belt.Speed;
                    }
                    phase = 3;
                    resumeFrame = Time.frameCount + 2;
                }
                else ReturnBatch();
            }
            else if (phase == 3)
            {
                VerifyCounts(count);
                for (int i = 0; i < count; i++)
                    if (ReferenceEquals(previousBeltStates[i], spawner.Items[i].BeltState)) throw new Exception("Conveyor loop did not run");
                ReturnBatch();
            }
            else if (phase == 4 && spawner.CanSpawn)
            {
                VerifyCounts(0);
                if (spawner.ReturnedCount != count || !spawner.CanRespawnReturned) throw new Exception("Return count/state mismatch");
                spawner.RespawnReturnedBatch();
                phase = 5;
            }
            else if (phase == 5)
            {
                if (spawner.MemoryStage != TEST_PerformanceSpawner.MemoryTestStage.Reused) return;
                VerifyCounts(count);
                if (spawner.ReusedObjectCount != count) throw new Exception("Expected existing pooled GameObjects");
                foreach (var item in spawner.Items)
                {
                    if (!originalObjects.Contains(item.GameObject.GetInstanceID()) || originalNetworkIds.Contains(item.NetworkId))
                        throw new Exception("Pool identity/new NetworkID mismatch");
                    if (comparison.UsesPairedPooling && (!IngredientEntityLifecycle.PooledSpawnSystem.TryGetPairedEntity(item.GameObject, out Entity entity) ||
                        entity != originalEntities[item.GameObject.GetInstanceID()])) throw new Exception("GameObject/Entity pair was not reused");
                }
                if (comparison.UsesPairedPooling)
                {
                    // A delayed event for a previous logical spawn must not retire its reused Entity.
                    foreach (long oldId in originalNetworkIds) IngredientEntityLifecycle.RequestDestroy(oldId);
                    phase = 7;
                    resumeFrame = Time.frameCount + 2;
                    return;
                }
                spawner.ClearSpawned();
                phase = 6;
                resumeFrame = Time.frameCount + 2;
            }
            else if (phase == 7)
            {
                VerifyCounts(count);
                spawner.ClearSpawned();
                phase = 6;
                resumeFrame = Time.frameCount + 2;
            }
            else if (phase == 6 && spawner.CanSpawn)
            {
                VerifyCounts(0);
                CompleteCase();
            }
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }

    private static void ReturnBatch()
    {
        if (comparison.UsesPairedPooling)
        {
            var manager = World.DefaultGameObjectInjectionWorld.EntityManager;
            foreach (Entity entity in originalEntities.Values)
            {
                manager.SetComponentData(entity, new Health { Current = -123, Max = 777 });
                manager.SetComponentData(entity, new NetworkRemoteSync { InterpolationSpeed = 777f });
            }
        }
        spawner.ClearSpawned();
        phase = 4;
        resumeFrame = Time.frameCount + 2;
    }

    private static void CompleteCase()
    {
        Debug.Log($"[ECSComparison Validation] PASS {comparison.ActiveBackend} case {caseIndex + 1}/10");
        caseIndex++;
        phase = 0;
    }

    private static void VerifyCounts(int expected)
    {
        if (spawner.UsesECS != comparison.UsesECS || ObjectPoolManager.Instance.activeObjDict.Count != expected ||
            bootstrap.Router.catchableDics.Count != expected || spawner.VerifiedObjectCount != expected)
            throw new Exception("Backend/GameObject/Router count mismatch");
        World world = World.DefaultGameObjectInjectionWorld;
        if (comparison.UsesECS)
        {
            if (world == null || !world.IsCreated) throw new Exception("Hybrid mode has no default ECS World");
            using var ingredients = comparison.UsesPairedPooling
                ? world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientInfo>(), ComponentType.ReadOnly<NetworkID>(), ComponentType.ReadOnly<IngredientPooledActive>())
                : world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientInfo>(), ComponentType.ReadOnly<NetworkID>());
            using var spawn = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientSpawnRequest>());
            using var destroy = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientEntityDestroyRequest>());
            if (ingredients.CalculateEntityCount() != expected || !spawn.IsEmptyIgnoreFilter || !destroy.IsEmptyIgnoreFilter || spawner.VerifiedEntityCount != expected)
                throw new Exception("Hybrid Entity/request/cleanup mismatch");
            if (comparison.UsesPairedPooling)
            {
                if (IngredientEntityLifecycle.PooledSpawnSystem.PendingBatchRequests != 0) throw new Exception("Batch buffer not drained");
                foreach (Entity entity in originalEntities.Values)
                {
                    if (!world.EntityManager.Exists(entity)) continue; // EmptyIngredientPool deliberately releases inactive pairs.
                    bool active = world.EntityManager.IsComponentEnabled<IngredientPooledActive>(entity);
                    long id = world.EntityManager.GetComponentData<NetworkID>(entity).Value;
                    if (expected == 0 && (active || id != 0 || world.EntityManager.GetComponentData<Health>(entity).Current != 0))
                        throw new Exception("Returned pooled Entity retained active state/identity/health");
                }
            }
        }
        else if (comparison.UsesCentralized)
        {
            if (world == null || !world.IsCreated) throw new Exception("Production comparison must retain the project's default World");
            using var ingredients = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientInfo>(), ComponentType.ReadOnly<NetworkID>());
            using var spawn = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientSpawnRequest>());
            using var destroy = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientEntityDestroyRequest>());
            if (!ingredients.IsEmptyIgnoreFilter || !spawn.IsEmptyIgnoreFilter || !destroy.IsEmptyIgnoreFilter ||
                spawner.PendingCentralRequests != 0 || spawner.VerifiedEntityCount != 0 ||
                world.GetExistingSystemManaged<IngredientSpawnSystem>() != null || world.GetExistingSystemManaged<IngredientEntityCleanupSystem>() != null)
                throw new Exception("Production GO path created ingredient Entities or installed a legacy system");
        }
        else if (world != null || spawner.VerifiedEntityCount != 0)
            throw new Exception("GameObject-only mode has ECS World/Entities or cleanup requests");
        foreach (var item in spawner.Items)
        {
            CatchableObj food = item.GameObject.GetComponent<CatchableObj>();
            if (food.NetworkId != item.NetworkId || food.NetworkId < TEST_PerformanceSpawner.FirstTestNetworkId || food.Data == null || food.Data.id != 10300 ||
                !bootstrap.Router.TryGet(item.NetworkId, out var routed) || routed != food)
                throw new Exception("GameObject data/identity/Router registration differs from the production path");
            if (spawner.UseConveyor && (item.Belt == null || !item.Belt.IsRegistered(item.NetworkId)))
                throw new Exception("Conveyor registration missing");
            if (comparison.UsesPairedPooling)
            {
                var system = IngredientEntityLifecycle.PooledSpawnSystem;
                if (!system.TryGetPairedEntity(item.GameObject, out Entity entity) || world.EntityManager.GetComponentData<NetworkID>(entity).Value != item.NetworkId ||
                    world.EntityManager.GetComponentData<IngredientInfo>(entity).ID != food.Data.id ||
                    world.EntityManager.GetComponentData<Health>(entity).Current != DataManager.Instance.GetIngredientStat().Get(DataManager.Instance.GetIngredient().Get(food.Data.id).statID).hp ||
                    world.EntityManager.GetComponentData<NetworkRemoteSync>(entity).InterpolationSpeed != 15f)
                    throw new Exception("Pooled Entity data/identity was not reinitialized");
            }
        }
        if (UnityEngine.Object.FindAnyObjectByType<Server.ServerManager>() != null) throw new Exception("Unexpected ServerManager");
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Finish(false, message + "\n" + stack);
    }

    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(RunningKey, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        Debug.Log($"[ECSComparison Validation] {(success ? "PASS" : "FAIL")}: {message}");
        EditorApplication.Exit(success ? 0 : 1);
    }
}
