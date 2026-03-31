using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class IngredientSpawnSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // 1. 데이터가 로드되었는지 확인 (DataManager 상태 체크)
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        // 2. 시스템이 여러 번 실행되지 않도록 비활성화
        Enabled = false;

        // 3. DataManager에서 구조체 리스트 가져오기
        // (앞서 만든 GetAll() 메서드를 사용합니다)
        var ingredientList = DataManager.Instance.GetIngredientAttributes().GetAll();
        int count = ingredientList.Count;

        if (count == 0)
        {
            Debug.LogWarning("[ECS] 생성할 재료 데이터가 없습니다.");
            return;
        }

        // 4. EntityManager 가져오기
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // 5. 엔티티 생성을 위한 네이티브 배열 준비 (성능 최적화)
        using var entities = new NativeArray<Entity>(count, Allocator.Temp);

        // 6-1. 아키타입 생성 (어떤 컴포넌트들을 가질지 미리 정의)
        EntityArchetype archetype = entityManager.CreateArchetype(typeof(IngredientAttribute));

        // 6-2. 아키타입을 기반으로 여러 엔티티 일괄 생성 (이게 올바른 순서입니다)
        entityManager.CreateEntity(archetype, entities);

        // 7. 각 엔티티에 시트 데이터 바인딩
        for (int i = 0; i < count; i++)
        {
            Entity entity = entities[i];
            IngredientAttribute data = ingredientList[i];

            // 엔티티에 실제 데이터(구조체) 주입
            entityManager.SetComponentData(entity, data);

            // 확인용 로그 (선택 사항)
            Debug.Log($"[ECS] 재료 생성 완료: {data.name.ToString()} (ID: {data.id})");
        }

        Debug.Log($"[ECS] 총 {count}개의 재료 엔티티가 성공적으로 바인딩되었습니다.");
    }

}
