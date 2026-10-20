using Unity.Collections;
using Unity.Entities;

/// <summary>
/// GameObject 재료의 NetworkID와 ECS 재료 엔티티의 생명주기를 연결합니다.
/// 풀 반환이나 네트워크 ID 교체 시 정리 요청을 만들고, ECS 시스템이 안전한 시점에 처리합니다.
/// </summary>
public struct IngredientEntityDestroyRequest : IComponentData
{
    public long NetworkId;
}

public static class IngredientEntityLifecycle
{
    private static ObjectPoolManager observedPool;
    public static bool ComparisonEnabled { get; private set; }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { DetachPool(); ComparisonEnabled = false; }

    public static void EnableComparison(World world, bool paired = false)
    {
        ComparisonEnabled = true;
        var spawn = world.GetOrCreateSystemManaged<IngredientSpawnSystem>();
        var cleanup = world.GetOrCreateSystemManaged<IngredientEntityCleanupSystem>();
        var simulation = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
        var initialization = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
        simulation.AddSystemToUpdateList(spawn);
        initialization.AddSystemToUpdateList(cleanup);
        simulation.SortSystems();
        initialization.SortSystems();
        if (paired) spawn.EnablePairedPooling(500);
    }

    public static void AttachPool(ObjectPoolManager pool)
    {
        DetachPool();
        observedPool = pool;
        pool.ObjectPrewarmed += Prepare;
        pool.ObjectDiscarding += Discard;
        pool.NetworkStateReleased += RequestDestroy;
    }

    public static void DetachPool()
    {
        if (observedPool != null)
        {
            observedPool.ObjectPrewarmed -= Prepare;
            observedPool.ObjectDiscarding -= Discard;
            observedPool.NetworkStateReleased -= RequestDestroy;
        }
        observedPool = null;
    }

    private static void Prepare(UnityEngine.GameObject go) => PooledSpawnSystem?.PreparePooledObject(go);
    private static void Discard(UnityEngine.GameObject go) => PooledSpawnSystem?.ReleasePooledObject(go);
    public static IngredientSpawnSystem PooledSpawnSystem
    {
        get
        {
            if (!ComparisonEnabled) return null;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;
            var system = world.GetExistingSystemManaged<IngredientSpawnSystem>();
            return system != null && system.PairedPoolingEnabled ? system : null;
        }
    }

    public static void RequestDestroy(long networkId)
    {
        if (!ComparisonEnabled) return;
        if (networkId <= 0) return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        if (PooledSpawnSystem?.TryReturnPooledEntity(networkId) == true) return;

        EntityManager entityManager = world.EntityManager;
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientEntityDestroyRequest));
        entityManager.SetComponentData(requestEntity, new IngredientEntityDestroyRequest
        {
            NetworkId = networkId
        });
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
[DisableAutoCreation]
public partial class IngredientEntityCleanupSystem : SystemBase
{
    private EntityQuery destroyRequestQuery;

    protected override void OnCreate()
    {
        destroyRequestQuery = GetEntityQuery(ComponentType.ReadOnly<IngredientEntityDestroyRequest>());
    }

    protected override void OnUpdate()
    {
        int requestCount = destroyRequestQuery.CalculateEntityCount();
        if (requestCount == 0) return;

        NativeHashSet<long> networkIds = new(requestCount, Allocator.Temp);
        EntityCommandBuffer ecb = new(Allocator.Temp);

        foreach ((RefRO<IngredientEntityDestroyRequest> request, Entity requestEntity) in
                 SystemAPI.Query<RefRO<IngredientEntityDestroyRequest>>().WithEntityAccess())
        {
            networkIds.Add(request.ValueRO.NetworkId);
            ecb.DestroyEntity(requestEntity);
        }

        foreach ((RefRO<NetworkID> networkId, Entity entity) in
                 SystemAPI.Query<RefRO<NetworkID>>().WithEntityAccess())
        {
            if (networkIds.Contains(networkId.ValueRO.Value))
            {
                // Also handle destroy requests supplied directly, rather than through RequestDestroy.
                if (IngredientEntityLifecycle.PooledSpawnSystem?.TryReturnPooledEntity(networkId.ValueRO.Value) != true)
                    ecb.DestroyEntity(entity);
            }
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
        networkIds.Dispose();
    }
}
