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
    public static event Action<CookCompletePacket> CookCompleted;

    [Header("Spawn Settings")]
    private readonly Define.eIngredient[] ingredientIDs = {
        //Define.eIngredient.Mushroom,
        //Define.eIngredient.Carrot,
        //Define.eIngredient.Tomato,
        Define.eIngredient.Fish,
        Define.eIngredient.Meat,
        //Define.eIngredient.Corn,
        //Define.eIngredient.Honey,
        //Define.eIngredient.Squid,
    };

    [SerializeField] private GameObject spawnPointObj;

    private void OnEnable()
    {
        Instance = this;

        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.RegisterHandler(PacketId.S_IngredientSpawn, OnIngredientSpawnReceived);
            ServerManager.Instance.RegisterHandler(PacketId.S_IngraedientConveySpawn, OnConveySpawnReceived);

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
            ServerManager.Instance.UnRegisterHandler(PacketId.S_IngraedientConveySpawn); 
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
            Rotation = new quaternion(packet.Quaternion.X, packet.Quaternion.Y, packet.Quaternion.Z, packet.Quaternion.W),
            ConveyId = 0 // 컨베이어 벨트용 스폰이 아니므로 사용 안 함 (기존 스폰 경로 유지)
        });
    }

    /// <summary>
    /// 컨베이어 벨트 위 재료 스폰 전용 핸들러.
    /// 이 패킷엔 Position/Rotation이 없으므로, ConveyId로 ConveyorSpawnPoint를 찾아
    /// 그 지점의 좌표/회전값을 대신 채워서 기존 IngredientSpawnRequest 흐름에 태운다.
    /// </summary>
    public void OnConveySpawnReceived(ReadOnlyMemory<byte> data)
    {
        if (DataManager.Instance == null || !DataManager.Instance.IsDataLoaded) return;

        IngredientConveySpawnPacket packet = MemoryPackSerializer.Deserialize<IngredientConveySpawnPacket>(data.Span);
        Debug.Log($"[Network] ConveySpawnPacket received. ConveyId: {packet.ConveyId}, IngredienteId: {packet.IngredienteId}, EntityId: {packet.EntityId}");

        if (!ConveyorSpawnPointRegistry.TryGetSpawnPoint(packet.ConveyId, out var spawnPoint))
        {
            Debug.LogWarning($"[Network] ConveyId {packet.ConveyId}에 해당하는 스폰 포인트를 씬에서 찾을 수 없습니다.");
            return;
        }

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity requestEntity = entityManager.CreateEntity(typeof(IngredientSpawnRequest));

        entityManager.SetComponentData(requestEntity, new IngredientSpawnRequest
        {
            IngredientID = packet.IngredienteId,
            NetworkID = packet.EntityId,
            Position = (float3)spawnPoint.Position,
            Rotation = (quaternion)spawnPoint.Rotation,
            ConveyId = packet.ConveyId
        });
    }
}