using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectNetworkRouter : Singleton<ObjectNetworkRouter>
{

    public Dictionary<long, CatchableObj> catchableDics = new();

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(PacketId.S_EntityDestroy, HandleEntityDestroy);
        ServerManager.Instance.RegisterHandler(PacketId.S_ToolSpawn, HandleToolSpawn);
    }

    private void HandleEntityDestroy(ReadOnlyMemory<byte> data)
    {
        EntityDestroyPacket packet = MemoryPackSerializer.Deserialize<EntityDestroyPacket>(data.Span);

        if (!catchableDics.TryGetValue(packet.EntityId, out CatchableObj catchable))
            return;

        catchable.IsRespawning = false;

        catchableDics.Remove(packet.EntityId);

        ObjectPoolManager.Instance.Push(catchable.gameObject);
    }

    private void HandleToolSpawn(ReadOnlyMemory<byte> data)
    {
        ToolSpawnPacket packet = MemoryPackSerializer.Deserialize<ToolSpawnPacket>(data.Span)!;

        string toolName = ((CatchableObjType)packet.ToolId).ToString(); // enum �̸��� prefab key�� ��ġ�Ѵٰ� ����
        Vector3 pos = ProtocolTypeConverter.ToUnityVector3(packet.Position);
        Quaternion rot = Quaternion.identity;

        GameObject go = ObjectPoolManager.Instance.Pop(toolName, pos, rot);
        if (go == null) return;

        if (go.TryGetComponent(out CatchableObj catchable))
        {
            catchable.NetworkId = packet.EntityId;
            Add(packet.EntityId, catchable);
            ObjectPoolManager.Instance.activeObjDict[packet.EntityId] = go;
            Debug.Log($"새로 꺼낸 tool network ID: {catchable.NetworkId} entity ID: {packet.EntityId}");
        }
    }

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

}
