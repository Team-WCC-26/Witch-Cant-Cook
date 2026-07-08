using Protocol;
using Server;
using System.Collections.Generic;
using Unity.Transforms;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static UnityEditor.FilePathAttribute;

public class TrashCan : MonoBehaviour
{
    private int playerLayer;
    private  int catchableLayer;

    [Header("Respawn Point")]
    public Transform kitchenSpawnPoint;

    private void Awake()
    {
        playerLayer = LayerMask.NameToLayer("Ragdoll");
        catchableLayer = LayerMask.NameToLayer("Catchable");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.layer == playerLayer)
        {
            RespawnPlayer(other);
            return;
        }

        if (other.gameObject.layer != catchableLayer)
            return;

        if (!other.TryGetComponent(out CatchableObj catchable))
            return;

        switch (catchable.ObjType)
        {
            case CatchableObjType.Ingredient:
                HandleIngredient(catchable);
                break;

            default:
                HandleTool(catchable);
                break;
        }
    }
    void RespawnPlayer(Collider other)
    {
        if (other.TryGetComponent(out PlayerBrain player))
        {
            PlayerSpawnManager.Instance.RespawnPlayer(player.PlayerId, kitchenSpawnPoint);
        }
    }
    void HandleTool(CatchableObj catchable)
    {
        if (catchable.IsRespawning) return;

        catchable.IsRespawning = true;

        Debug.Log("HandleTool - Start");

        EntityDestroyPacket packet = new()
        {
            EntityId = catchable.NetworkId
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));

        Debug.Log($"HandleTool - TrashCan: {catchable.ObjType} {catchable.NetworkId} is thrown into trash can.");

        ToolSpawnPacket toolSpawnPacket = new()
        {
            EntityId = 0,
            ToolId = (int)catchable.ObjType,
            Position = new System.Numerics.Vector3(kitchenSpawnPoint.position.x, kitchenSpawnPoint.position.y, kitchenSpawnPoint.position.z),
            Quaternion = new System.Numerics.Quaternion(kitchenSpawnPoint.rotation.x, kitchenSpawnPoint.rotation.y, kitchenSpawnPoint.rotation.z, kitchenSpawnPoint.rotation.w)
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(toolSpawnPacket));
        Debug.Log("HandleTool - End");
    }

    void HandleIngredient(CatchableObj catchable)
    {
        EntityDestroyPacket packet = new()
        {
            EntityId = catchable.NetworkId
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

}
