using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectRouter : Singleton<ObjectRouter>
{

    public Dictionary<long, CatchableObj> catchableDics = new();
    private readonly Queue<CatchableObj> registerQueue = new();

    public void EnqueueRegister(CatchableObj obj)
    {
        registerQueue.Enqueue(obj);
    }

    public CatchableObj DequeueRegister()
    {
        return registerQueue.Dequeue();
    }

    public int RegisterQueueCount => registerQueue.Count;

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
