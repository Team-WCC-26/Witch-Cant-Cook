using Cysharp.Threading.Tasks;
using MemoryPack;
using Protocol;
using Server;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class GlobalSpawnManager : MonoBehaviour
{
    private readonly int[] ingredientIDs = { 10900, 10300, 11900, 12000, 10600 };
    private long fakeNetworkIdCounter = 1; // 테스트용 가짜 서버 ID 카운터

    private void Update()
    {
        // F1 키: 클라이언트 단독 스폰 테스트
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            int randomID = ingredientIDs[UnityEngine.Random.Range(0, ingredientIDs.Length)];
            SendSpawnPacketToServer(randomID);
        }

        // ESC 키: 마우스 커서 락 해제
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // 로컬 스폰 요청 생성
    public void SpawnTestIngredient(int ingredientID)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        float3 targetPosition = GetSpawnPosition();

        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientSpawnRequest));

        entityManager.SetComponentData(requestEntity, new IngredientSpawnRequest
        {
            IngredientID = ingredientID,
            NetworkID = 0, // 로컬 생성은 서버 ID가 없으므로 0 처리
            Position = targetPosition,
            Rotation = quaternion.identity 
        });
        Debug.Log($"[Test] F1 입력 감지! 로컬 스폰 요청 생성. ID: {ingredientID}");
    }

    public void SendSpawnPacketToServer(int ingredientID)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        float3 targetPosition = GetSpawnPosition();

        var spawnPacket = new IngredientSpawnPacket
        {
            EntityId = 0, // 클라이언트 요청 단계이므로 0 고정
            IngredientID = ingredientID,
            Position = new System.Numerics.Vector3(targetPosition.x, targetPosition.y, targetPosition.z),
            Quaternion = System.Numerics.Quaternion.Identity
        };

        byte[] sendBuffer = PacketSerializer.Serialize(spawnPacket);

        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.SendData(sendBuffer).Forget();

            Debug.Log($"[Network Test] 서버로 IngredientSpawnPacket 전송 완료! 재료 ID: {ingredientID}, 좌표: {targetPosition}");
        }
        else
        {
            Debug.LogError("[Network Test] ServerManager.Instance를 찾을 수 없습니다. 씬에 배치되었는지 확인하세요.");
        }
    }
    private float3 GetSpawnPosition()
    {
        GameObject spawnPointObj = GameObject.Find("SpawnPoint");
        return spawnPointObj != null ? (float3)spawnPointObj.transform.position : float3.zero;
    }
}