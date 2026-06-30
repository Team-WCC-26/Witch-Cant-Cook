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
        // �ʹ� �ӽ���
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            SpawnTool("Knife");
        }
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            SpawnTool("Plate");
        }

    }

    private void SpawnTool(string tool)
    {
        ToolSpawnPacket packet = new()
        {
            EntityId = 0, // ������ �ο�
            ToolId = tool switch
            {
                "Knife" => (int)CatchableObjType.Knife,
                "Plate" => (int)CatchableObjType.Plate
            },
            Position = new System.Numerics.Vector3(SpawnPos.transform.position.x, SpawnPos.transform.position.y, SpawnPos.transform.position.z),
            Quaternion = new System.Numerics.Quaternion(SpawnPos.transform.rotation.x, SpawnPos.transform.rotation.y, SpawnPos.transform.rotation.z, SpawnPos.transform.rotation.w)
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public bool RespawnTool(CatchableObj tool, Transform spawnPoint)
    {
        if (tool == null || spawnPoint == null)
            return false;

        return true;
    }
}