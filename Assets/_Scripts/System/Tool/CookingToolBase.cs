using UnityEngine;
using Unity.Entities;
using Unity.Physics;
using Unity.Collections;

public abstract class CookingToolBase : MonoBehaviour
{
    public float interactionRadius = 1.0f;
    public CollisionFilter filter = CollisionFilter.Default;

    protected EntityManager EntityManager => World.DefaultGameObjectInjectionWorld.EntityManager;

    // 공통 공간 쿼리 로직: 내 주변의 엔티티들을 찾아 리스트로 반환
    protected void ScanAndProcess()
    {
        var physicsWorld = EntityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton)).GetSingleton<PhysicsWorldSingleton>();
        NativeList<DistanceHit> hits = new NativeList<DistanceHit>(Allocator.Temp);

        if (physicsWorld.CollisionWorld.OverlapSphere(transform.position, interactionRadius, ref hits, filter))
        {
            foreach (var hit in hits)
            {
                // 재료 엔티티인 경우에만 자식 클래스에서 정의한 로직 실행
                if (EntityManager.HasComponent<Health>(hit.Entity))
                {
                    OnIngredientDetected(hit.Entity);
                }
            }
        }
        hits.Dispose();
    }

    // 자식 클래스에서 "어떤 요리를 할지" 구현 (추상 메서드)
    protected abstract void OnIngredientDetected(Entity ingredient);
}