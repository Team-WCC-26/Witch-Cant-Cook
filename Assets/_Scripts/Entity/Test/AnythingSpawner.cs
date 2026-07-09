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
            SpawnTool((int)CatchableObjType.Knife);
        }
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            SpawnTool((int)CatchableObjType.Plate);
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
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }
}