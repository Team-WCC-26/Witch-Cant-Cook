using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
// 새로운 입력 시스템 네임스페이스 추가
using UnityEngine.InputSystem;

public class GlobalSpawnManager : MonoBehaviour
{
    public int testIngredientID = 10100;

    private void Update()
    {
        // Keyboard.current를 사용하여 입력 확인
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            SpawnTestIngredient();
        }

        // ESC 키로 커서 복구 (필요시)
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void SpawnTestIngredient()
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        GameObject spawnPointObj = GameObject.Find("SpawnPoint");
        float3 targetPosition = spawnPointObj != null ? (float3)spawnPointObj.transform.position : float3.zero;

        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientSpawnRequest));

        entityManager.SetComponentData(requestEntity, new IngredientSpawnRequest
        {
            IngredientID = testIngredientID,
            Position = targetPosition
        });

        Debug.Log($"[Test] InputSystem 감지! ID {testIngredientID} 스폰.");
    }
}