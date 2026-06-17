using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectNetworkRouter : Singleton<ObjectNetworkRouter>
{

    public Dictionary<long, CatchableObj> catchableDics = new();
    private readonly Queue<CatchableObj> registerQueue = new();
    public int RegisterQueueCount => registerQueue.Count;

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(PacketId.S_ToolRegister, HandleToolRegister);
        ServerManager.Instance.RegisterHandler(PacketId.S_ToolSpawn, HandleToolSpawn);
    }


    #region Object Register
    public void EnqueueRegister(CatchableObj obj)
    {
        registerQueue.Enqueue(obj);
    }

    private void HandleToolRegister(ReadOnlyMemory<byte> data)
    {
        Debug.Log($"Register Queue Count = {registerQueue.Count}");
        ToolRegisterPacket packet = MemoryPackSerializer.Deserialize<ToolRegisterPacket>(data.Span)!;

        if (registerQueue.Count == 0)
        {
            Debug.LogError($"등록 대기중인 CatchableObj 없음. EntityId={packet.EntityId}");
            return;
        }

        CatchableObj obj = registerQueue.Dequeue();

        obj.NetworkId = packet.EntityId;

        Add(packet.EntityId, obj);

        Debug.Log($"Tool Registered : {obj.name} -> {packet.EntityId}");
    }
    private void HandleToolSpawn(ReadOnlyMemory<byte> data)
    {
        ToolSpawnPacket packet = MemoryPackSerializer.Deserialize<ToolSpawnPacket>(data.Span)!;

        string toolName = ((CatchableObjType)packet.ToolId).ToString(); // enum 이름이 prefab key와 일치한다고 가정
        Vector3 pos = ProtocolTypeConverter.ToUnityVector3(packet.Position);
        Quaternion rot = new Quaternion(packet.Quaternion.X, packet.Quaternion.Y, packet.Quaternion.Z, packet.Quaternion.W);

        GameObject go = ObjectPoolManager.Instance.Pop(toolName, pos, rot);
        if (go == null) return;

        if (go.TryGetComponent(out CatchableObj catchable))
        {
            catchable.NetworkId = packet.EntityId;
            Add(packet.EntityId, catchable);
            ObjectPoolManager.Instance.activeObjDict[packet.EntityId] = go;
        }
    }
    public CatchableObj DequeueRegister()
    {
        return registerQueue.Dequeue();
    }

    #endregion


    // 일단 catchableObj만 관리
    // 나중에 다른 컴포넌트 생기면 그때 추가
    public void Add(long networkId, CatchableObj obj)
    {
        catchableDics[networkId] = obj;
    }

    public void Remove(long networkId)
    {
        catchableDics.Remove(networkId);
    }
    public bool TryGet(long networkId, out CatchableObj obj)
    {
        return catchableDics.TryGetValue(networkId, out obj);
    }

    //private void RegisterSceneObjects()
    //{
    //    CatchableObj[] objs =
    //        FindObjectsByType<CatchableObj>(
    //            FindObjectsSortMode.None);

    //    foreach (var obj in objs)
    //    {
    //        Add(obj.NetworkId, obj);
    //    }
    //}
}
