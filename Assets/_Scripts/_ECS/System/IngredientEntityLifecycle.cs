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
    public static void RequestDestroy(long networkId)
    {
        if (networkId <= 0) return;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return;

        EntityManager entityManager = world.EntityManager;
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientEntityDestroyRequest));
        entityManager.SetComponentData(requestEntity, new IngredientEntityDestroyRequest
        {
            NetworkId = networkId
        });
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
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
                ecb.DestroyEntity(entity);
            }
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
        networkIds.Dispose();
    }
}
