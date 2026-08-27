using Protocol;
using Server;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities.UniversalDelegates;
using UnityEngine;
using UnityEngine.InputSystem;

public class AnythingSpawner : MonoBehaviour
{
    [SerializeField] private GameObject SpawnPos;

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            Debug.Log($"[AnythingSpawner] f2 키 입력");

            SpawnTool((int)Define.eToolId.KitchenKnife);
        }
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            Debug.Log($"[AnythingSpawner] f3 키 입력");

            SpawnTool((int)(int)Define.eToolId.Plate);
        }
        if (Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame)
        {
            Debug.Log($"[AnythingSpawner] f4 키 입력");

            SpawnTool((int)(int)Define.eToolId.FryingPan);
        }
    }

    private void SpawnTool(int toolKey)
    {
        ToolSpawnPacket packet = new()
        {
            EntityId = 0, 
            ToolId = toolKey,
            Position = new System.Numerics.Vector3(SpawnPos.transform.position.x, SpawnPos.transform.position.y, SpawnPos.transform.position.z),
            Quaternion = System.Numerics.Quaternion.Identity
        };
        Debug.Log($"[AnythingSpawner] Sending ToolSpawnPacket: ToolId={packet.ToolId}, Position={packet.Position}");
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }
}