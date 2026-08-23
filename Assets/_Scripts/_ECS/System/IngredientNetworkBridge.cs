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
    public static IngredientNetworkBridge Instance { get; private set; }
    [Header("Spawn Settings")]
    private readonly Define.eIngredient[] ingredientIDs = {
        //Define.eIngredient.Mushroom,
        //Define.eIngredient.Carrot,
        //Define.eIngredient.Tomato,
        //Define.eIngredient.Fish,
        //Define.eIngredient.Meat,
        //Define.eIngredient.Corn,
        //Define.eIngredient.Honey,
        //Define.eIngredient.Squid,
        //Define.eIngredient.Onion,
        Define.eIngredient.Salmon,
    };

    [SerializeField] private GameObject spawnPointObj;

    private void OnEnable()
    {
        Instance = this;

        if (ServerManager.Instance != null)
        {
            // Spawn packets only
            ServerManager.Instance.RegisterHandler(PacketId.S_IngredientSpawn, OnIngredientSpawnReceived);

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
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            int randomID = (int)ingredientIDs[UnityEngine.Random.Range(0, ingredientIDs.Length)];
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

    /// <summary>
    /// �ٸ� position���� �����ؾ��� �ʿ䰡 ���� �� ���
    /// </summary>
    /// <param name="ingredientID"></param>
    /// <param name="pos"></param>
    public void SendSpawnPacketToServer(int ingredientID, float3 pos)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        float3 targetPosition = pos;

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

    }

}
