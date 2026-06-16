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
        // 너무 임시임
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
        GameObject go = ObjectPoolManager.Instance.Pop(tool, SpawnPos.transform.position, SpawnPos.transform.rotation);
        Debug.Log(go);
        if (go.TryGetComponent(out CatchableObj catchable))
        {
            catchable.NetworkId = Time.frameCount;
            ObjectRouter.Instance.Add(catchable.NetworkId, catchable);
            ObjectPoolManager.Instance.activeObjDict.Add(catchable.NetworkId, go);
        }

        ToolSpawnPacket packet = new()
        {
            EntityId = go.GetComponent<CatchableObj>().NetworkId,
            ToolId = (int)CatchableObjType.Knife,
            Position = new System.Numerics.Vector3(go.transform.position.x, go.transform.position.y, go.transform.position.z),
            Quaternion = new System.Numerics.Quaternion(go.transform.rotation.x, go.transform.rotation.y, go.transform.rotation.z, go.transform.rotation.w)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }
}