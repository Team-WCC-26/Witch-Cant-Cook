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
        ServerManager.Instance.RegisterHandler(PacketId.S_ToolSpawn, HandleToolSpawn);
    }

    private void HandleToolSpawn(ReadOnlyMemory<byte> data)
    {
        ToolSpawnPacket packet = MemoryPackSerializer.Deserialize<ToolSpawnPacket>(data.Span)!;

        string toolName = ((CatchableObjType)packet.ToolId).ToString(); // enum �̸��� prefab key�� ��ġ�Ѵٰ� ����
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

    // �ϴ� catchableObj�� ����
    // ���߿� �ٸ� ������Ʈ ����� �׶� �߰�
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
