using MemoryPack;
using Protocol; // 패킷이 포함된 네임스페이스 추가
using Server;
using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class IngredientNetworkManager : MonoBehaviour
{
    private void OnEnable()
    {
        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.RegisterHandler(PacketId.S_IngredientSpawn, OnIngredientSpawnReceived);
        }
    }

    private void OnDisable()
    {
        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.UnRegisterHandler(PacketId.S_IngredientSpawn);
        }
    }

    private void OnIngredientSpawnReceived(ReadOnlyMemory<byte> data)
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
            NetworkID = packet.NetworkID, // 패킷의 EntityId와 매핑

            Position = new float3(packet.Position.x, packet.Position.y, packet.Position.z),

            Rotation = new quaternion(packet.Quaternion.x, packet.Quaternion.y, packet.Quaternion.z, packet.Quaternion.w)
        });

        Debug.Log($"[Network] 서버 패킷 수신 - ID: {packet.IngredientID}, NetID: {packet.NetworkID}");
    }
}