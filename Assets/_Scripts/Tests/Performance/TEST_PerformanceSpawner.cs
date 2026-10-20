using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

/// <summary>Uses production GO spawning by default. Historical backends exist only in the comparison scene.</summary>
[DisallowMultipleComponent]
public sealed partial class TEST_PerformanceSpawner : MonoBehaviour
{
    public const long FirstTestNetworkId = 1_000_000_000;
    public const string AllocatedMemoryCounterName = "TEST.Memory.Total Allocated Memory";
    private static ProfilerCounterValue<long> allocatedMemoryCounter = new(
        ProfilerCategory.Memory, AllocatedMemoryCounterName, ProfilerMarkerDataUnit.Bytes);
    private static readonly ProfilerMarker EnqueueMarker = new("TEST.Performance.EnqueueSpawnRequests");
    private static readonly ProfilerMarker VerifyMarker = new("TEST.Performance.VerifySpawn");
    private static readonly ProfilerMarker ClearMarker = new("TEST.Performance.Clear");
    private static readonly ProfilerMarker PrewarmMarker = new("TEST.Performance.Prewarm");
    private static readonly ProfilerMarker ZeroMarker = new("TEST.Memory.Zero");
    private static readonly ProfilerMarker SpawnedMarker = new("TEST.Memory.Spawned");
    private static readonly ProfilerMarker ReturnedMarker = new("TEST.Memory.Returned");
    private static readonly ProfilerMarker ReusedMarker = new("TEST.Memory.Reused");
    private static readonly ProfilerMarker ComparisonSubmitMarker = new("TEST.Comparison.SubmitBatch");
    private static readonly ProfilerMarker GameObjectSpawnMarker = new("TEST.Comparison.GameObjectOnly.Spawn");
    private const int InitialTrackingCapacity = 500;

    public enum MemoryTestStage { Ready, Zero, Spawning, Spawned, Returning, Returned, Reusing, Reused, Failed }

    [SerializeField] private TEST_PerformanceBootstrap bootstrap;
    [Header("Ingredient")]
    [SerializeField] private int ingredientId = 10300;
    [SerializeField, Min(0)] private int spawnCount = 50;
    [SerializeField] private bool autoSpawn = true;
    [Tooltip("0 = cold pool. Prewarm is completed before automatic request creation.")]
    [SerializeField, Min(0)] private int prewarmCount;
    [Header("Floor placement (world coordinates)")]
    [SerializeField] private Vector3 startPosition = new(-48f, 1f, -20f);
    [SerializeField] private Vector3 spacing = new(2f, 0f, 2f);
    [SerializeField] private bool useGrid = true;
    [SerializeField, Min(1)] private int gridColumns = 25;
    [Header("Conveyor placement (overrides floor placement)")]
    [SerializeField] private bool useConveyor;
    [SerializeField] private ConveyorBeltController[] conveyorBelts;
    [SerializeField, Min(0f)] private float conveyorStartDistance = 2f;
    [SerializeField, Min(0.01f)] private float conveyorSpacing = 2f;

    public sealed class SpawnedItem
    {
        internal int IntervalSlot;
        public long NetworkId;
        public GameObject GameObject;
        public ConveyorBeltController Belt;
        public ConveyorItemState BeltState;
    }

    private readonly List<long> ownedIds = new(InitialTrackingCapacity);
    private readonly HashSet<long> ownedIdSet = new(InitialTrackingCapacity);
    private readonly List<SpawnedItem> items = new(InitialTrackingCapacity);
    private readonly List<SpawnedItem> itemCache = new(InitialTrackingCapacity);
    private readonly HashSet<int> pooledInstanceIds = new(InitialTrackingCapacity);
    private long nextNetworkId = FirstTestNetworkId;
    private World world;
    private EntityQuery requestQuery;
    private EntityQuery ingredientQuery;
    private EntityQuery destroyQuery;
    private bool queriesCreated;
    private bool awaitingSpawn;
    private bool awaitingCleanup;
    private float spawnDeadline;
    private int submittedIngredientId;
    private int returnedIngredientId;
    private Transform returnedPoolRoot;
    private bool reusingReturnedBatch;
    private TEST_ECSComparison comparison;
    private bool useEcs;
    private bool useCentralized;
    private IngredientSpawnProcessor centralSpawner;
    private IngredientSpawnSystem pairedSpawnSystem;
    private IngredientSpawnRequest[] comparisonRequests;
    private bool gameObjectBatchPending;

    public bool IsReady => bootstrap != null && bootstrap.IsReady;
    public bool CanSpawn => Application.isPlaying && IsReady && ownedIds.Count == 0 && !awaitingCleanup && !IntervalSessionActive;
    public bool HasBatch => ownedIds.Count > 0;
    public bool UseConveyor => useConveyor;
    public bool UsesECS => useEcs;
    public bool UsesCentralized => useCentralized;
    public int PendingCentralRequests => centralSpawner != null ? centralSpawner.PendingCount : 0;
    public bool AutoSpawn => autoSpawn;
    public int IngredientId => ingredientId;
    public int SpawnCount { get => spawnCount; set => spawnCount = Mathf.Max(0, value); }
    public MemoryTestStage MemoryStage { get; private set; }
    public bool IsBusy => awaitingSpawn || awaitingCleanup || IntervalSessionActive;
    public int RequestedCount => ownedIds.Count;
    public int ReturnedCount { get; private set; }
    public int ReusedObjectCount { get; private set; }
    public int PairedEntityCapacity => pairedSpawnSystem?.PairedEntityCapacity ?? -1;
    public int PairedEntitiesCreated => pairedSpawnSystem?.PairedEntitiesCreated ?? -1;
    public int PairedEntitiesReused => pairedSpawnSystem?.PairedEntitiesReused ?? -1;
    public bool CanRespawnReturned => CanSpawn && ReturnedCount > 0 && returnedPoolRoot != null;
    public int VerifiedObjectCount { get; private set; }
    public int VerifiedEntityCount { get; private set; }
    public string Status { get; private set; } = "Waiting for bootstrap";
    public IReadOnlyList<SpawnedItem> Items => items;

    private void Awake()
    {
        comparison = GetComponent<TEST_ECSComparison>();
        useEcs = comparison != null && comparison.UsesECS;
        useCentralized = comparison == null || comparison.UsesCentralized;
        if (useCentralized)
        {
            centralSpawner = gameObject.AddComponent<IngredientSpawnProcessor>();
            // The CSV Prewarm stage owns preparation; no automatic production warm-up in tests.
            centralSpawner.Configure(bootstrap.Router, false);
        }
        if (comparison != null && comparison.UsesPairedPooling)
            pairedSpawnSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<IngredientSpawnSystem>();
        // The same managed staging capacity is present in both comparison modes.
        if (comparison != null) comparisonRequests = new IngredientSpawnRequest[InitialTrackingCapacity];
    }

    private IEnumerator Start()
    {
        if (bootstrap == null) { Fail("Assign TEST_PerformanceBootstrap."); yield break; }
        while (!bootstrap.IsReady && !bootstrap.HasFailed) yield return null;
        if (!bootstrap.IsReady) { Status = bootstrap.Status; yield break; }
        // Prepare test bookkeeping before the zero baseline; retain it across return/reuse steps.
        EnsureQueries();
        PrepareItemCache(InitialTrackingCapacity);
        Status = "Ready";
        if (autoSpawn)
        {
            if (prewarmCount > 0 && !PrewarmPool()) yield break;
            SpawnConfigured();
        }
    }

    public void SpawnConfigured()
    {
        if (!CanSpawn) { Fail("Wait for Ready, then Clear the previous batch before spawning."); return; }
        if (!useEcs && !useCentralized && comparison != null && World.DefaultGameObjectInjectionWorld != null)
        { Fail("GameObject-only mode requires no default ECS World. Restart Play."); return; }
        if (spawnCount == 0)
        {
            using (ZeroMarker.Auto())
            {
                MemoryStage = MemoryTestStage.Zero;
                Status = "Zero: 0 active ingredients (existing pool and loaded assets retained)";
            }
            return;
        }
        if (!TryGetIngredient(out _)) return;
        EnsureQueries();
        if (spawnCount < 1 || gridColumns < 1 || nextNetworkId > long.MaxValue - spawnCount)
        { Fail("Invalid spawn count, grid columns, or exhausted NetworkID range."); return; }

        // Validate the complete batch before issuing any requests. Never silently replace an existing identity.
        for (int i = 0; i < spawnCount; i++)
        {
            long id = nextNetworkId + i;
            if (ObjectPoolManager.Instance.activeObjDict.ContainsKey(id) || bootstrap.Router.catchableDics.ContainsKey(id) ||
                ConveyorBeltRegistry.IsOwnedByAnyBelt(id))
            { Fail($"NetworkID collision: {id}. Batch was not submitted."); return; }
            if (useConveyor && !ValidateBeltSlot(i)) return;
        }
        if (useEcs)
        {
            using var networkIds = ingredientQuery.ToComponentDataArray<NetworkID>(Allocator.Temp);
            using var requests = requestQuery.ToComponentDataArray<IngredientSpawnRequest>(Allocator.Temp);
            for (int i = 0; i < spawnCount; i++)
            {
                long id = nextNetworkId + i;
                foreach (NetworkID existing in networkIds)
                    if (existing.Value == id) { Fail($"ECS NetworkID collision: {id}."); return; }
                foreach (IngredientSpawnRequest request in requests)
                    if (request.NetworkID == id) { Fail($"Pending NetworkID collision: {id}."); return; }
            }
        }

        ownedIds.Capacity = Mathf.Max(ownedIds.Capacity, spawnCount);
        items.Capacity = Mathf.Max(items.Capacity, spawnCount);
        PrepareItemCache(spawnCount);
        if (comparisonRequests != null && comparisonRequests.Length < spawnCount)
            Array.Resize(ref comparisonRequests, spawnCount);
        submittedIngredientId = ingredientId;
        ReusedObjectCount = 0;
        if (!reusingReturnedBatch)
        {
            ReturnedCount = 0;
            pooledInstanceIds.Clear();
        }
        // Allocate test bookkeeping outside the marker that measures request creation.
        for (int i = 0; i < spawnCount; i++)
        {
            ownedIds.Add(nextNetworkId + i);
            ownedIdSet.Add(nextNetworkId + i);
        }
        using ((comparison != null ? ComparisonSubmitMarker : EnqueueMarker).Auto())
        {
            EntityManager manager = useEcs ? world.EntityManager : default;
            for (int i = 0; i < spawnCount; i++)
            {
                IngredientSpawnRequest data = CreateSpawnData(i);
                if (comparisonRequests != null) comparisonRequests[i] = data;
                if (useCentralized) centralSpawner.Enqueue(new IngredientSpawnCommand
                {
                    IngredientID = data.IngredientID, NetworkID = data.NetworkID,
                    Position = data.Position, Rotation = data.Rotation, ConveyId = data.ConveyId
                });
                if (!useEcs || pairedSpawnSystem != null) continue;
                Entity request = manager.CreateEntity(typeof(IngredientSpawnRequest));
                manager.SetComponentData(request, data);
            }
            pairedSpawnSystem?.EnqueueBatch(comparisonRequests, spawnCount);
        }
        gameObjectBatchPending = !useEcs && !useCentralized;
        nextNetworkId += spawnCount;
        awaitingSpawn = true;
        MemoryStage = reusingReturnedBatch ? MemoryTestStage.Reusing : MemoryTestStage.Spawning;
        spawnDeadline = Time.realtimeSinceStartup + 15f;
        Status = $"Submitted {spawnCount} {(useEcs ? "ECS requests" : "GameObject spawn records")} in one frame";
    }

    private IngredientSpawnRequest CreateSpawnData(int index)
    {
        ConveyorBeltController belt = useConveyor ? conveyorBelts[index % conveyorBelts.Length] : null;
        Vector3 position;
        Quaternion rotation = Quaternion.identity;
        if (belt != null)
        {
            var sample = belt.Path.Evaluate(conveyorStartDistance + index / conveyorBelts.Length * conveyorSpacing);
            position = sample.point;
            if (sample.tangent.sqrMagnitude > 0.0001f) rotation = Quaternion.LookRotation(sample.tangent, Vector3.up);
        }
        else
            position = startPosition + (useGrid
                ? new Vector3(index % gridColumns * spacing.x, 0f, index / gridColumns * spacing.z)
                : spacing * index);
        return new IngredientSpawnRequest
        {
            IngredientID = ingredientId,
            NetworkID = ownedIds[index],
            Position = (float3)position,
            Rotation = (quaternion)rotation,
            ConveyId = belt != null ? belt.BeltId : 0
        };
    }

    private bool ProcessGameObjectBatch()
    {
        gameObjectBatchPending = false;
        using var scope = GameObjectSpawnMarker.Auto();
        // Mirror IngredientSpawnSystem's ingredient GameObject initialization/registration order.
        // Only request Entities, ingredient Entities and ECB playback are omitted. No direct Instantiate.
        for (int i = 0; i < ownedIds.Count; i++)
        {
            if (!SpawnGameObjectRecord(comparisonRequests[i])) return false;
        }
        return true;
    }

    private bool SpawnGameObjectRecord(IngredientSpawnRequest data)
    {
        if (!DataManager.Instance.GetIngredient().TryGet(data.IngredientID, out Ingredient ingredient))
        { awaitingSpawn = false; Fail($"Ingredient data missing: {data.IngredientID}."); return false; }
        Vector3 position = data.Position;
        Quaternion rotation = data.Rotation;
        GameObject go = ObjectPoolManager.Instance.Pop(ingredient.prefabName, position, rotation,
            out CatchableObj catchable, out IngredientReaction reaction);
        if (go == null)
        { awaitingSpawn = false; Fail($"Pool spawn failed: {ingredient.prefabName}."); return false; }
        FoodRecipeIdentity.Initialize(catchable, ingredient, null);
        reaction?.ResetForPool();
        catchable.NetworkId = data.NetworkID;
        bootstrap.Router.Add(data.NetworkID, catchable);
        ObjectPoolManager.Instance.activeObjDict.Add(data.NetworkID, go);
        IngredientEgg.PrepareSpawnedInside(data.IngredientID, position, catchable);
        if (ConveyorBeltRegistry.TryGetBeltById(data.ConveyId, out ConveyorBeltController belt) && belt != null)
            belt.RegisterItem(data.NetworkID, go.transform, go);
        return true;
    }

    private void Update()
    {
        if (IntervalSessionActive) { VerifyIntervalSpawns(); return; }
        if (useEcs)
        {
            if (!queriesCreated || world == null || !world.IsCreated) return;
        }
        else
        {
            if (!useCentralized && comparison != null && World.DefaultGameObjectInjectionWorld != null)
            {
                if (MemoryStage != MemoryTestStage.Failed) Fail("An ECS World was recreated in GameObject-only mode. Restart Play.");
                return;
            }
            if (gameObjectBatchPending && !ProcessGameObjectBatch()) return;
        }
        if (awaitingCleanup)
        {
            if (!useEcs || destroyQuery.IsEmptyIgnoreFilter)
            {
                using (ReturnedMarker.Auto())
                {
                    awaitingCleanup = false;
                    MemoryStage = MemoryTestStage.Returned;
                    Status = useEcs
                        ? $"Returned: {ReturnedCount} to pool / 0 active / ECS cleanup complete"
                        : $"Returned: {ReturnedCount} to pool / 0 active / no ingredient Entities";
                }
            }
            return;
        }
        if (!awaitingSpawn) return;
        if (useCentralized && centralSpawner.PendingCount > 0)
        {
            if (Time.realtimeSinceStartup >= spawnDeadline)
            { awaitingSpawn = false; Fail("Central GO requests were not consumed. Clear before retrying."); }
            return;
        }
        if (useEcs && (!requestQuery.IsEmptyIgnoreFilter || (pairedSpawnSystem != null && pairedSpawnSystem.PendingBatchRequests > 0)))
        {
            if (Time.realtimeSinceStartup >= spawnDeadline)
            { awaitingSpawn = false; Fail("Spawn requests were not consumed. Check IngredientSpawnSystem and Console. Clear before retrying."); }
            return;
        }

        using (VerifyMarker.Auto())
        {
            awaitingSpawn = false;
            VerifiedObjectCount = 0;
            VerifiedEntityCount = 0;
            for (int i = 0; i < ownedIds.Count; i++)
            {
                if (!ObjectPoolManager.Instance.activeObjDict.TryGetValue(ownedIds[i], out UnityEngine.Object obj) ||
                    obj is not GameObject go || !go.activeInHierarchy) continue;
                ConveyorBeltController belt = useConveyor ? conveyorBelts[i % conveyorBelts.Length] : null;
                ConveyorItemState state = null;
                if (belt != null && !belt.TryGetItem(ownedIds[i], out state))
                { Fail($"Conveyor registration missing for {ownedIds[i]}."); return; }
                SpawnedItem item = itemCache[i];
                item.NetworkId = ownedIds[i];
                item.GameObject = go;
                item.Belt = belt;
                item.BeltState = state;
                items.Add(item);
                if (reusingReturnedBatch && pooledInstanceIds.Contains(go.GetInstanceID())) ReusedObjectCount++;
                VerifiedObjectCount++;
            }
            if (useEcs)
            {
                using var ids = ingredientQuery.ToComponentDataArray<NetworkID>(Allocator.Temp);
                foreach (NetworkID id in ids)
                    if (ownedIdSet.Contains(id.Value)) VerifiedEntityCount++;
            }
            if (VerifiedObjectCount != ownedIds.Count || VerifiedEntityCount != (useEcs ? ownedIds.Count : 0))
            { Fail($"Spawn mismatch: requested {ownedIds.Count}, GameObjects {VerifiedObjectCount}, Entities {VerifiedEntityCount}. Clear before retrying."); return; }
            if (reusingReturnedBatch && ReusedObjectCount != ownedIds.Count)
            {
                reusingReturnedBatch = false;
                Fail($"Reuse mismatch: {ReusedObjectCount}/{ownedIds.Count} were existing inactive pool objects. Clear before retrying.");
                return;
            }
            using ((reusingReturnedBatch ? ReusedMarker : SpawnedMarker).Auto())
            {
                MemoryStage = reusingReturnedBatch ? MemoryTestStage.Reused : MemoryTestStage.Spawned;
                Status = reusingReturnedBatch
                    ? $"Reused: {ReusedObjectCount}/{ownedIds.Count} existing GameObjects / {VerifiedEntityCount} {(pairedSpawnSystem != null ? "reused" : "new")} ECS Entities"
                    : $"Spawned: {VerifiedObjectCount} GameObjects / {VerifiedEntityCount} Entities";
            }
            reusingReturnedBatch = false;
            pooledInstanceIds.Clear();
        }
        Debug.Log($"[PerformanceTest] {Status}. Steady-state measurement can begin after physics settles.", this);
    }

    private void LateUpdate()
    {
#if ENABLE_PROFILER
        // Native counter only: no strings, collection scans, or managed allocations per frame.
        if (IsReady)
            allocatedMemoryCounter.Value = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
#endif
    }

    public void ClearSpawned()
    {
        if (!Application.isPlaying || !IsReady || !HasBatch) return;
        using (ClearMarker.Auto())
        {
            awaitingSpawn = false;
            gameObjectBatchPending = false;
            if (useCentralized) centralSpawner.CancelPending();
            reusingReturnedBatch = false;
            MemoryStage = MemoryTestStage.Returning;
            ReturnedCount = 0;
            ReusedObjectCount = 0;
            returnedIngredientId = submittedIngredientId;
            if (useEcs)
            {
                pairedSpawnSystem?.CancelBatchRequests();
                EntityManager manager = world.EntityManager;
                using var requests = requestQuery.ToComponentDataArray<IngredientSpawnRequest>(Allocator.Temp);
                using var entities = requestQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < requests.Length; i++)
                    if (ownedIdSet.Contains(requests[i].NetworkID)) manager.DestroyEntity(entities[i]);
            }
            foreach (long id in ownedIds)
            {
                if (ObjectPoolManager.Instance.activeObjDict.TryGetValue(id, out UnityEngine.Object obj) && obj is GameObject go && go != null)
                {
                    ObjectPoolManager.Instance.Push(go);
                    if (go != null && !go.activeSelf)
                    {
                        ReturnedCount++;
                        returnedPoolRoot = go.transform.parent;
                    }
                }
                else
                {
                    if (ConveyorBeltRegistry.TryGetOwner(id, out ConveyorBeltController belt)) belt.UnregisterItem(id);
                    bootstrap.Router.Remove(id);
                    ObjectPoolManager.Instance.activeObjDict.Remove(id);
                    IngredientEntityLifecycle.RequestDestroy(id);
                }
            }
            // Do not retain food/ConveyorItemState references in test records after pool return.
            for (int i = 0; i < items.Count; i++)
            {
                items[i].NetworkId = 0;
                items[i].GameObject = null;
                items[i].Belt = null;
                items[i].BeltState = null;
            }
            items.Clear();
            pooledInstanceIds.Clear();
            ownedIds.Clear();
            ownedIdSet.Clear();
            VerifiedObjectCount = VerifiedEntityCount = 0;
            awaitingCleanup = true;
            Status = useEcs ? "Waiting for existing ECS cleanup system" : "Returning GameObjects (no ECS cleanup requests)";
        }
    }

    public void RespawnReturnedBatch()
    {
        if (!CanRespawnReturned) { Fail("Return a batch and wait for ECS cleanup before reusing it."); return; }
        // The FIFO pool may contain older returned or prewarmed objects. Snapshot all inactive
        // pool children without allocating a FindObjects array or keeping GameObject references.
        pooledInstanceIds.Clear();
        for (int i = 0; i < returnedPoolRoot.childCount; i++)
        {
            GameObject pooled = returnedPoolRoot.GetChild(i).gameObject;
            if (!pooled.activeSelf) pooledInstanceIds.Add(pooled.GetInstanceID());
        }
        ingredientId = returnedIngredientId;
        spawnCount = ReturnedCount;
        reusingReturnedBatch = true;
        SpawnConfigured();
        if (!awaitingSpawn) reusingReturnedBatch = false;
    }

    private void PrepareItemCache(int count)
    {
        for (int i = itemCache.Count; i < count; i++) itemCache.Add(new SpawnedItem());
    }

    public bool PrewarmPool() => PrewarmPool(prewarmCount);

    public bool PrewarmPool(int count)
    {
        if (!CanSpawn || !TryGetIngredient(out Ingredient ingredient)) return false;
        ObjectPoolManager pool = ObjectPoolManager.Instance;
        int previous = pool.GetPooledCount(ingredient.prefabName);
        using (PrewarmMarker.Auto()) pool.PrewarmPool(ingredient.prefabName, count);
        if (pool.GetPooledCount(ingredient.prefabName) != previous + Mathf.Max(0, count))
        { Fail("Pool prewarm did not retain the requested objects."); return false; }
        Status = $"Prewarmed {count} additional objects";
        return true;
    }

    public void EmptyIngredientPool()
    {
        if (!CanSpawn || !TryGetIngredient(out Ingredient ingredient)) return;
        ObjectPoolManager.Instance.ClearPool(ingredient.prefabName);
        ReturnedCount = 0;
        returnedPoolRoot = null;
        pooledInstanceIds.Clear();
        MemoryStage = MemoryTestStage.Zero;
        Status = "Pool emptied. Spawn on the next frame for a cold test";
    }

    private bool TryGetIngredient(out Ingredient ingredient)
    {
        ingredient = null;
        if (!IsReady) return false;
        if (!DataManager.Instance.GetIngredient().TryGet(ingredientId, out ingredient) ||
            DataManager.Instance.GetIngredientStat().Get(ingredient.statID) == null)
        { Fail($"Ingredient/Stat data missing for {ingredientId}."); return false; }
        if (!ResourceManager.Instance.TryGetAsset<GameObject>(ingredient.prefabName, out var prefab) ||
            prefab.GetComponent<CatchableObj>() == null)
        { Fail($"Loaded CatchableObj prefab missing: {ingredient.prefabName}."); return false; }
        // ConveyId 0 is the existing non-conveyor request convention.
        if (!useConveyor && ConveyorBeltRegistry.TryGetBeltById(0, out _))
        { Fail("Belt ID 0 is reserved for floor requests. Use positive IDs for test belts."); return false; }
        return true;
    }

    private bool ValidateBeltSlot(int index)
    {
        if (conveyorBelts == null || conveyorBelts.Length == 0 || conveyorSpacing <= 0f || conveyorStartDistance < 0f)
        { Fail("Assign conveyors and positive spacing."); return false; }
        ConveyorBeltController belt = conveyorBelts[index % conveyorBelts.Length];
        if (belt == null || !belt.isActiveAndEnabled || belt.BeltId <= 0 || belt.Path == null || belt.Speed <= 0f ||
            !ConveyorBeltRegistry.TryGetBeltById(belt.BeltId, out var registered) || registered != belt)
        { Fail("Every test belt needs a unique positive ID, a path, and positive speed."); return false; }
        if (conveyorStartDistance + index / conveyorBelts.Length * conveyorSpacing >= belt.Path.TotalLength)
        { Fail("Batch exceeds conveyor length. Add lanes or reduce spacing/count."); return false; }
        return true;
    }

    private void EnsureQueries()
    {
        if (queriesCreated || !useEcs) return;
        world = World.DefaultGameObjectInjectionWorld;
        requestQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientSpawnRequest>());
        ingredientQuery = pairedSpawnSystem != null
            ? world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkID>(), ComponentType.ReadOnly<IngredientInfo>(), ComponentType.ReadOnly<IngredientPooledActive>())
            : world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkID>(), ComponentType.ReadOnly<IngredientInfo>());
        destroyQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<IngredientEntityDestroyRequest>());
        queriesCreated = true;
    }

    private void Fail(string message)
    {
        Status = message;
        MemoryStage = MemoryTestStage.Failed;
        Debug.LogError($"[PerformanceTest] {message}", this);
    }

    private void OnDestroy()
    {
        if (!queriesCreated || world == null || !world.IsCreated) return;
        requestQuery.Dispose();
        ingredientQuery.Dispose();
        destroyQuery.Dispose();
    }
}
