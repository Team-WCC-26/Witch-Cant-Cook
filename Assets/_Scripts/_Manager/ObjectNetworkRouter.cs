using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IEntityParentReceiver
{
    void HandleEntityAdded(CatchableObj entity);
    void HandleEntityRemoved(CatchableObj entity);
}

public class ObjectNetworkRouter : Singleton<ObjectNetworkRouter>
{
    private const int TrashIngredientId = 99999;

    public Dictionary<long, CatchableObj> catchableDics = new();

    [SerializeField] private MapObjNetworkRouter mapObjRouter;

    private Coroutine subscribeRoutine;
    private bool isSubscribed;

    private void OnEnable()
    {
        subscribeRoutine = StartCoroutine(SubscribeWhenReady());
    }

    private IEnumerator SubscribeWhenReady()
    {
        // Packet ownership
        yield return new WaitUntil(() => ServerManager.Instance != null);

        if (mapObjRouter == null)
            mapObjRouter = FindFirstObjectByType<MapObjNetworkRouter>();

        ServerManager.Instance.Router.OnEntityParentChanged += HandleEntityParentChanged;
        ServerManager.Instance.Router.OnEntityDestroyed += HandleEntityDestroyed;
        ServerManager.Instance.Router.OnCookProcessChanged += HandleCookProcessChanged;
        ServerManager.Instance.RegisterHandler(PacketId.S_EntityThrow, HandleEntityThrow);
        ServerManager.Instance.RegisterHandler(PacketId.S_EntityDestroy, HandleEntityDestroy);
        ServerManager.Instance.RegisterHandler(PacketId.S_ToolSpawn, HandleToolSpawn);
        isSubscribed = true;
        subscribeRoutine = null;
    }

    private void OnDisable()
    {
        if (subscribeRoutine != null)
        {
            StopCoroutine(subscribeRoutine);
            subscribeRoutine = null;
        }

        if (!isSubscribed || ServerManager.Instance == null) return;

        ServerManager.Instance.Router.OnEntityParentChanged -= HandleEntityParentChanged;
        ServerManager.Instance.Router.OnEntityDestroyed -= HandleEntityDestroyed;
        ServerManager.Instance.Router.OnCookProcessChanged -= HandleCookProcessChanged;
        ServerManager.Instance.UnRegisterHandler(PacketId.S_EntityThrow);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_EntityDestroy);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_ToolSpawn);
        isSubscribed = false;
    }

    private void HandleEntityDestroy(ReadOnlyMemory<byte> data)
    {
        EntityDestroyPacket packet = MemoryPackSerializer.Deserialize<EntityDestroyPacket>(data.Span);
        RemoveAndRelease(packet.EntityId);
    }

    private void HandleEntityDestroyed(IReadOnlyList<EntityDestroyPacket> packets)
    {
        foreach (EntityDestroyPacket packet in packets)
            RemoveAndRelease(packet.EntityId);
    }

    private void HandleEntityParentChanged(IReadOnlyList<EntityChangeParentPacket> packets)
    {
        // Parent transition
        foreach (EntityChangeParentPacket packet in packets)
        {
            if (!catchableDics.TryGetValue(packet.EntityId, out CatchableObj catchable))
            {
                Debug.LogError($"Parent change target not found. EntityId: {packet.EntityId}");
                continue;
            }

            long previousParentId = catchable.ParentEntityId;
            if (previousParentId == packet.ParentEntityId)
                continue;

            NotifyParent(previousParentId, catchable, false);
            catchable.ParentEntityId = packet.ParentEntityId;
            NotifyParent(packet.ParentEntityId, catchable, true);
        }
    }

    private void HandleCookProcessChanged(IReadOnlyList<CookProcessPacket> packets)
    {
        foreach (CookProcessPacket packet in packets)
        {
            if (!catchableDics.TryGetValue(packet.EntityId, out CatchableObj catchable))
                continue;

            if (catchable.TryGetComponent(out IngredientReaction reaction))
                reaction.GaugeUI?.ShowProgress(packet.Process);
        }
    }

    private void HandleEntityThrow(ReadOnlyMemory<byte> data)
    {
        EntityThrowPacket packet = MemoryPackSerializer.Deserialize<EntityThrowPacket>(data.Span);

        if (!catchableDics.TryGetValue(packet.EntityId, out CatchableObj catchable))
        {
            Debug.LogError($"Throw target not found. EntityId: {packet.EntityId}");
            return;
        }

        catchable.Holder?.Interact.ApplyThrown(catchable);
        catchable.ParentEntityId = 0;
        catchable.ApplyThrow(packet);
    }

    private void RemoveAndRelease(long entityId)
    {
        if (!catchableDics.TryGetValue(entityId, out CatchableObj catchable))
            return;

        catchable.IsRespawning = false;

        catchableDics.Remove(entityId);
        ObjectPoolManager.Instance.activeObjDict.Remove(entityId);
        catchable.ReleaseCombinedVisual();

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

    public bool TryApplyIngredientCombine(
        IngredientCombinePacket packet,
        out CatchableObj result,
        out CatchableObj secondaryVisual)
    {
        // Identity replacement
        result = null;
        secondaryVisual = null;

        if (!TryGet(packet.SubjectEntityId, out CatchableObj subject) ||
            !TryGet(packet.TargetEntityId, out CatchableObj target))
            return false;

        Ingredient resultData = DataManager.Instance?.GetIngredient().GetData(packet.ResultIngredientId);
        if (resultData == null)
        {
            Debug.LogError($"Combine data not found. IngredientId: {packet.ResultIngredientId}");
            return false;
        }

        long parentEntityId = subject.ParentEntityId;

        if (packet.ResultIngredientId == TrashIngredientId)
        {
            // Trash replacement
            GameObject trashObject = ObjectPoolManager.Instance.Pop(
                resultData.prefabName,
                subject.transform.position,
                subject.transform.rotation);

            if (trashObject == null || !trashObject.TryGetComponent(out CatchableObj trash))
            {
                Debug.LogError("Trash prefab not found.");
                return false;
            }

            Unregister(packet.SubjectEntityId);
            Unregister(packet.TargetEntityId);
            ObjectPoolManager.Instance.Push(subject.gameObject);
            ObjectPoolManager.Instance.Push(target.gameObject);

            trash.NetworkId = packet.NewEntityId;
            trash.ParentEntityId = parentEntityId;
            trash.Data = resultData;
            Register(packet.NewEntityId, trash);
            result = trash;
            return true;
        }

        Unregister(packet.SubjectEntityId);
        Unregister(packet.TargetEntityId);

        subject.NetworkId = packet.NewEntityId;
        subject.ParentEntityId = parentEntityId;
        subject.Data = resultData;
        target.NetworkId = 0;
        target.ParentEntityId = 0;
        target.ChangePickState(false);
        target.SetPhysicsState(false);
        // Visual ownership
        subject.AttachCombinedVisual(target);

        Register(packet.NewEntityId, subject);
        result = subject;
        secondaryVisual = target;
        return true;
    }

    private void NotifyParent(long parentEntityId, CatchableObj entity, bool added)
    {
        // Container notification
        if (parentEntityId == 0) return;

        if (!TryResolveParent(parentEntityId, out IEntityParentReceiver receiver))
        {
            Debug.LogError($"Parent receiver not found. EntityId: {parentEntityId}");
            return;
        }

        if (added) receiver.HandleEntityAdded(entity);
        else receiver.HandleEntityRemoved(entity);
    }

    private bool TryResolveParent(long entityId, out IEntityParentReceiver receiver)
    {
        receiver = null;
        MonoBehaviour parent = null;

        if (mapObjRouter != null && mapObjRouter.TryGetMapObject(entityId, out MapObjInteraction mapObj))
            parent = mapObj;
        else if (TryGet(entityId, out CatchableObj catchable))
            parent = catchable;

        if (parent == null) return false;
        receiver = parent.GetComponent<IEntityParentReceiver>();
        return receiver != null;
    }

    private void Unregister(long entityId)
    {
        catchableDics.Remove(entityId);
        ObjectPoolManager.Instance.activeObjDict.Remove(entityId);
    }

    private void Register(long entityId, CatchableObj catchable)
    {
        catchableDics[entityId] = catchable;
        ObjectPoolManager.Instance.activeObjDict[entityId] = catchable.gameObject;
    }
}
