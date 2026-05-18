using MemoryPack;
using Protocol;
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
            SpawnTestIngredient(randomID);
        }

        // F2 키: 가짜 서버 패킷 수신 시뮬레이션 테스트
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            int randomID = ingredientIDs[UnityEngine.Random.Range(0, ingredientIDs.Length)];
            SimulateServerPacket(randomID);
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

    // 가짜 서버 패킷 송신 시뮬레이션
    private void SimulateServerPacket(int ingredientID)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        float3 targetPosition = GetSpawnPosition();

        // 서버가 보낼 직렬화 데이터 패킷 모사
        var fakePacket = new IngredientSpawnPacket
        {
            IngredientID = ingredientID,
            NetworkID = fakeNetworkIdCounter++, // 누를 때마다 1, 2, 3... 증가
            Position = targetPosition
        };

        // 가짜 패킷 byte[] 직렬화
        byte[] rawBuffer = MemoryPackSerializer.Serialize(fakePacket);

        // 씬 내의 네트워크 핸들러를 찾아 강제로 패킷 데이터 주입
        var networkHandler = FindFirstObjectByType<IngredientNetworkManager>();
        if (networkHandler != null)
        {
            networkHandler.SendMessage("OnIngredientSpawnReceived", (System.ReadOnlyMemory<byte>)rawBuffer);
            Debug.Log($"[Test] F2 입력 감지! 가짜 서버 패킷 핸들러로 전달 완료 (NetID: {fakePacket.NetworkID})");
        }
    }

    private float3 GetSpawnPosition()
    {
        GameObject spawnPointObj = GameObject.Find("SpawnPoint");
        return spawnPointObj != null ? (float3)spawnPointObj.transform.position : float3.zero;
    }
}