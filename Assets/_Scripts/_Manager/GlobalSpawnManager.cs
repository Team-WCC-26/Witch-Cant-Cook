using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class GlobalSpawnManager : MonoBehaviour
{
    // 버튼에서 이 함수를 호출하게 하세요.
    public void SpawnTestIngredient()
    {
        // 1. 데이터 로딩 확인
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded)
        {
            Debug.LogWarning("DataManager가 아직 준비되지 않았습니다.");
            return;
        }

        // 2. 씬에서 "SpawnPoint"라는 이름을 가진 오브젝트를 찾습니다.
        // (인스펙터 할당 대신 이름으로 찾기)
        GameObject spawnPointObj = GameObject.Find("SpawnPoint");
        float3 targetPosition = float3.zero;

        if (spawnPointObj != null)
        {
            targetPosition = spawnPointObj.transform.position;
        }
        else
        {
            Debug.LogError("씬에 'SpawnPoint'라는 이름의 오브젝트가 없습니다! (0,0,0)에 소환합니다.");
        }

        // 3. ECS EntityManager 가져오기
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        // 4. 요청 엔티티(주문서) 생성 및 데이터 설정
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientSpawnRequest));

        entityManager.SetComponentData(requestEntity, new IngredientSpawnRequest
        {
            IngredientID = 101, // 테스트할 시트 ID를 직접 입력하세요.
            Position = targetPosition
        });

        Debug.Log($"[SpawnManager] ID 101 재료를 {targetPosition} 위치에 스폰 요청함.");
    }
}