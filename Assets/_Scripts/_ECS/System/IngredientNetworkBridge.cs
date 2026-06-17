using System;
using Cysharp.Threading.Tasks;
using MemoryPack;
using Protocol; // 패킷이 포함된 네임스페이스
using Server;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class IngredientNetworkBridge : MonoBehaviour
{
    [Header("Spawn Settings")]
    private readonly int[] ingredientIDs = { 
        //10900, 10300, 11900, 12000, 10600, 
        10900,
        12100,
        12300
    };
    [SerializeField] private GameObject spawnPointObj;
    private void OnEnable()
    {
        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.RegisterHandler(PacketId.S_IngredientSpawn, OnIngredientSpawnReceived);
            ServerManager.Instance.RegisterHandler(PacketId.S_EntityThrow, OnThrowReceived);

            Debug.Log("[Network] 패킷 핸들러 등록 완료");
        }
        else
        {
            Debug.LogError("[Network Error] ServerManager 없음");
        }
    }

    private void OnDisable()
    {
        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.UnRegisterHandler(
                PacketId.S_IngredientSpawn);

            ServerManager.Instance.UnRegisterHandler(
                PacketId.S_EntityThrow);
        }
    }

    private void Update()
    {
        // F1 키: 서버에 재료 스폰 요청 생성 패킷 송신
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            Debug.LogWarning("[TEST] F1 키 입력 감지 - 서버에 스폰 요청 패킷 전송 시도");
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

    // Client -> Server
    public void SendSpawnPacketToServer(int ingredientID)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        float3 targetPosition = GetSpawnPosition();

        var spawnPacket = new IngredientSpawnPacket
        {
            EntityId = 0, // 클라이언트의 생성 요청은 0
            IngredientID = ingredientID,
            Position = new System.Numerics.Vector3(targetPosition.x, targetPosition.y, targetPosition.z),
            Quaternion = System.Numerics.Quaternion.Identity
        };

        byte[] sendBuffer = PacketSerializer.Serialize(spawnPacket);

        if (ServerManager.Instance != null)
        {
            // 비동기로 서버에 패킷 전송
            ServerManager.Instance.SendData(sendBuffer).Forget();
            Debug.Log($"[Network Send] 서버로 스폰 요청 전송 완료 ID: {ingredientID}, 좌표: {targetPosition}");
        }
        else
        {
            Debug.LogError("[Network Error] ServerManager.Instance를 찾을 수 없습니다.");
        }
    }

    private float3 GetSpawnPosition()
    {
        return spawnPointObj != null ? (float3)spawnPointObj.transform.position : float3.zero;
    }

    // Server -> Client
    public void OnIngredientSpawnReceived(ReadOnlyMemory<byte> data)
    {
        Debug.Log("[TEST] Ingredient Packet Received");
        Debug.Log(World.DefaultGameObjectInjectionWorld);
        Debug.Log("[TEST] OnIngredientSpawnReceived 호출됨");
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        var packet = MemoryPackSerializer.Deserialize<IngredientSpawnPacket>(data.Span);

        // 스폰 요청 엔티티 생성
        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientSpawnRequest));
        Debug.Log($"[TEST] Request Entity 생성 완료 : {requestEntity.Index}");

        entityManager.SetComponentData(requestEntity, new IngredientSpawnRequest
        {
            IngredientID = packet.IngredientID,
            NetworkID = packet.EntityId,

            Position = new float3(packet.Position.X, packet.Position.Y, packet.Position.Z),
            Rotation = new quaternion(packet.Quaternion.X, packet.Quaternion.Y, packet.Quaternion.Z, packet.Quaternion.W)
        });

        Debug.Log($"[Network Recv] 서버 패킷 수신 성공 - ID: {packet.IngredientID}, NetID: {packet.EntityId}");
    }

    private void OnThrowReceived(ReadOnlyMemory<byte> data)
    {
        var packet = MemoryPackSerializer.Deserialize<EntityThrowPacket>(data.Span);

        if (!ObjectNetworkRouter.Instance.TryGet(packet.EntityId, out var catchable))
        {
            Debug.LogWarning($"NetworkID {packet.EntityId} 찾기 실패");
            return;
        }

        catchable.ApplyThrow(packet);
    }

}