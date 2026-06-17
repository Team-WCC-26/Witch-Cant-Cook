using System;
using Cysharp.Threading.Tasks;
using MemoryPack;
using Protocol;
using Server;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class IngredientNetworkBridge : MonoBehaviour
{
    public static event Action<CookCompletePacket> CookCompleted;

    [Header("Spawn Settings")]
    private readonly int[] ingredientIDs = {
        10600,
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
            ServerManager.Instance.RegisterHandler(PacketId.S_CookComplete, OnCookCompleteReceived);

            Debug.Log("[Network] Packet handlers registered.");
        }
        else
        {
            Debug.LogError("[Network Error] ServerManager missing.");
        }
    }

    private void OnDisable()
    {
        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.UnRegisterHandler(PacketId.S_IngredientSpawn);
            ServerManager.Instance.UnRegisterHandler(PacketId.S_EntityThrow);
            ServerManager.Instance.UnRegisterHandler(PacketId.S_CookComplete);
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            int randomID = ingredientIDs[UnityEngine.Random.Range(0, ingredientIDs.Length)];
            SendSpawnPacketToServer(randomID);
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void SendSpawnPacketToServer(int ingredientID)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        float3 targetPosition = GetSpawnPosition();

        IngredientSpawnPacket spawnPacket = new()
        {
            EntityId = 0,
            IngredientID = ingredientID,
            Position = new System.Numerics.Vector3(targetPosition.x, targetPosition.y, targetPosition.z),
            Quaternion = System.Numerics.Quaternion.Identity
        };

        byte[] sendBuffer = PacketSerializer.Serialize(spawnPacket);

        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.SendData(sendBuffer).Forget();
            Debug.Log($"[Network Send] Ingredient spawn requested. ID: {ingredientID}, Position: {targetPosition}");
        }
        else
        {
            Debug.LogError("[Network Error] ServerManager.Instance not found.");
        }
    }

    public static void RequestCookStart(long entityId, IngredientState cookType)
    {
        if (ServerManager.Instance == null)
        {
            Debug.LogWarning("Cannot request cook start because ServerManager.Instance is null.");
            return;
        }

        CookStartPacket packet = new()
        {
            EntityId = entityId,
            CookType = cookType
        };

        ServerManager.Instance.SendData(PacketSerializer.Serialize(packet)).Forget();
    }

    private float3 GetSpawnPosition()
    {
        return spawnPointObj != null ? (float3)spawnPointObj.transform.position : float3.zero;
    }

    public void OnIngredientSpawnReceived(ReadOnlyMemory<byte> data)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        IngredientSpawnPacket packet = MemoryPackSerializer.Deserialize<IngredientSpawnPacket>(data.Span);

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientSpawnRequest));

        entityManager.SetComponentData(requestEntity, new IngredientSpawnRequest
        {
            IngredientID = packet.IngredientID,
            NetworkID = packet.EntityId,
            Position = new float3(packet.Position.X, packet.Position.Y, packet.Position.Z),
            Rotation = new quaternion(packet.Quaternion.X, packet.Quaternion.Y, packet.Quaternion.Z, packet.Quaternion.W)
        });

        Debug.Log($"[Network Recv] Ingredient spawned. ID: {packet.IngredientID}, NetID: {packet.EntityId}");
    }

    private void OnThrowReceived(ReadOnlyMemory<byte> data)
    {
        EntityThrowPacket packet = MemoryPackSerializer.Deserialize<EntityThrowPacket>(data.Span);

        if (!ObjectNetworkRouter.Instance.TryGet(packet.EntityId, out CatchableObj catchable))
        {
            Debug.LogWarning($"NetworkID {packet.EntityId} not found.");
            return;
        }

        catchable.ApplyThrow(packet);
    }

    private void OnCookCompleteReceived(ReadOnlyMemory<byte> data)
    {
        CookCompletePacket packet = MemoryPackSerializer.Deserialize<CookCompletePacket>(data.Span);
        CookCompleted?.Invoke(packet);
    }
}
