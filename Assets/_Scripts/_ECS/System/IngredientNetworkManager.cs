using MemoryPack;
using Protocol; // 패킷이 포함된 네임스페이스 추가
using Server;
using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class IngredientNetworkManager : MonoBehaviour
{
    private void Start()
    {
        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.RegisterHandler(PacketId.S_IngredientSpawn, OnIngredientSpawnReceived);
            Debug.Log("[Network] S_IngredientSpawn 핸들러가 ServerManager에 정상 등록되었습니다.");
        }
        else
        {
            Debug.LogError("[Network] ServerManager 인스턴스를 찾을 수 없어 핸들러 등록에 실패했습니다.");
        }
    }
    //private void OnEnable()
    //{
    //    if (ServerManager.Instance != null)
    //    {
    //        ServerManager.Instance.RegisterHandler(PacketId.S_IngredientSpawn, OnIngredientSpawnReceived);
    //    }
    //}

    private void OnDisable()
    {
        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.UnRegisterHandler(PacketId.S_IngredientSpawn);
        }
    }

    public void OnIngredientSpawnReceived(ReadOnlyMemory<byte> data)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        // MemoryPack 역직렬화
        var packet = MemoryPackSerializer.Deserialize<IngredientSpawnPacket>(data.Span);

        var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientSpawnRequest));

        // System.Numerics -> Unity.Mathematics 형변환 주입
        entityManager.SetComponentData(requestEntity, new IngredientSpawnRequest
        {
            IngredientID = packet.IngredientID,
            NetworkID = packet.EntityId, // 패킷의 EntityId와 매핑

            Position = new float3(packet.Position.X, packet.Position.Y, packet.Position.Z),

            Rotation = new quaternion(packet.Quaternion.X, packet.Quaternion.Y, packet.Quaternion.Z, packet.Quaternion.W)
        });

        Debug.Log($"[Network] 서버 패킷 수신 - ID: {packet.IngredientID}, NetID: {packet.EntityId}");
    }
}