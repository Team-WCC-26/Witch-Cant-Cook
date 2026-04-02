using Unity.Entities;
using UnityEngine;

// 1. Authoring 스크립트 (MonoBehaviour)
public class IngredientSpawnerAuthoring : MonoBehaviour
{
    public GameObject ingredientPrefab; // 인스펙터에서 아까 만든 구 프리팹 할당

    // 2. 베이킹 클래스
    class Baker : Baker<IngredientSpawnerAuthoring>
    {
        public override void Bake(IngredientSpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            // 3. GameObject 프리팹을 Entity 프리팹으로 변환
            AddComponent(entity, new IngredientSpawnConfig
            {
                prefabEntity = GetEntity(authoring.ingredientPrefab, TransformUsageFlags.Dynamic)
            });
        }
    }
}

// 4. ECS 시스템에서 읽을 컴포넌트
public struct IngredientSpawnConfig : IComponentData
{
    public Entity prefabEntity;
}