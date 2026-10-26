using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectNetworkRouter : MonoBehaviour
{
    private const int TrashIngredientId = 99999;

    public Dictionary<long, CatchableObj> catchableDics = new();
    private readonly Dictionary<long, long> pendingParentChanges = new();
    private readonly Dictionary<long, IEntityThrowParentReceiver> detachedThrowParents = new();
    private readonly Dictionary<long, int> dishEjectSeeds = new();
    private readonly Dictionary<long, long> containerEjections = new();

    [SerializeField] private MapObjNetworkRouter mapObjRouter;

    private Coroutine subscribeRoutine;
    private bool isSubscribed;

    // Only after the server confirms its round entities have been cleared.
    public void ResetClientStage()
    {
        var ids = new List<long>(catchableDics.Keys);
        foreach (long id in ids) RemoveAndRelease(id);
        catchableDics.Clear();
        pendingParentChanges.Clear();
        detachedThrowParents.Clear();
        dishEjectSeeds.Clear();
        containerEjections.Clear();
        IngredientEgg.ResetPanTransfers();
    }

    private void Awake()
    {
        if (mapObjRouter == null)
            mapObjRouter = GetComponent<MapObjNetworkRouter>();
    }

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
        ServerManager.Instance.RegisterHandler(PacketId.S_DishEject, HandleDishEject);
        ServerManager.Instance.RegisterHandler(PacketId.S_PanEject, HandlePanEject);
        isSubscribed = true;
        subscribeRoutine = null;
    }

    private void OnDisable()
    {
        containerEjections.Clear();
        detachedThrowParents.Clear();
        dishEjectSeeds.Clear();
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
        ServerManager.Instance.UnRegisterHandler(PacketId.S_DishEject);
        ServerManager.Instance.UnRegisterHandler(PacketId.S_PanEject);
        isSubscribed = false;
    }

    private void HandleEntityDestroy(ReadOnlyMemory<byte> data)
    {
        EntityDestroyPacket packet = MemoryPackSerializer.Deserialize<EntityDestroyPacket>(data.Span);
        RemoveAndRelease(packet.EntityId);
    }

    private void HandleDishEject(ReadOnlyMemory<byte> data)
    {
        DishEjectPacket packet = PacketSerializer.Deserialize<DishEjectPacket>(data);
        if (dishEjectSeeds.TryGetValue(packet.DishEntityId, out int seed) && seed == packet.EjectSeed) return;
        if (!catchableDics.TryGetValue(packet.DishEntityId, out CatchableObj plate) ||
            plate == null || !plate.TryGetComponent<PlateInteraction>(out _)) return;
        if (SubmitTriggerZone.PlayConfirmedPlateReturn(plate))
            dishEjectSeeds[packet.DishEntityId] = packet.EjectSeed;
    }

    private void HandleEntityDestroyed(IReadOnlyList<EntityDestroyPacket> packets)
    {
        foreach (EntityDestroyPacket packet in packets)
            RemoveAndRelease(packet.EntityId);
    }

    private void HandlePanEject(ReadOnlyMemory<byte> data)
    {
        PanEjectPacket packet = PacketSerializer.Deserialize<PanEjectPacket>(data);
        if (!catchableDics.TryGetValue(packet.PanEntityId, out CatchableObj panObj) ||
            panObj == null || !panObj.TryGetComponent(out PanInteraction pan))
        {
            NetworkResponseWait.Error("Pan eject", packet.PanEntityId, "Pan not found");
            return;
        }
        if (!catchableDics.TryGetValue(packet.IngredientEntityId, out CatchableObj ingredient) ||
            ingredient == null || !ingredient.TryGetComponent<IngredientReaction>(out _))
        {
            NetworkResponseWait.Error("Pan eject", packet.IngredientEntityId, "Ingredient not found");
            return;
        }

        pan.TryApplyPanEject(packet, ingredient, this);
    }

    private void HandleEntityParentChanged(IReadOnlyList<EntityChangeParentPacket> packets)
    {
        // Parent transition
        foreach (EntityChangeParentPacket packet in packets)
        {
            if (!catchableDics.TryGetValue(packet.EntityId, out CatchableObj catchable))
            {
                pendingParentChanges[packet.EntityId] = packet.ParentEntityId;
                continue;
            }

            ApplyParentChange(catchable, packet.ParentEntityId);
        }
    }

    // 부모 변경 적용
    private void ApplyParentChange(CatchableObj catchable, long parentEntityId)
    {
        if (parentEntityId == -1) parentEntityId = 0;
        if (catchable.ParentEntityId == -1) catchable.ParentEntityId = 0;
        // 배출 결과보다 늦게 도착한 과거 삽입 결과로 다시 보관하지 않는다.
        if (containerEjections.TryGetValue(catchable.NetworkId, out long ejectParentId))
        {
            if (parentEntityId == ejectParentId) return;
            if (parentEntityId != 0) containerEjections.Remove(catchable.NetworkId);
        }

        long previousParentId = catchable.ParentEntityId;
        if (previousParentId == parentEntityId) return;
        if (parentEntityId != 0) PanInteraction.ForgetToss(catchable.NetworkId);

        detachedThrowParents.Remove(catchable.NetworkId);
        if (parentEntityId == 0 && TryResolveParent(previousParentId, out IEntityParentReceiver previousParent) &&
            previousParent is IEntityThrowParentReceiver throwParent)
            detachedThrowParents[catchable.NetworkId] = throwParent;

        NotifyParent(previousParentId, catchable, false);

        if (catchable.Holder != null)
        {
            catchable.Holder.Interact.TryReleaseHeld(catchable);
            catchable.OnDrop();
        }

        catchable.ParentEntityId = parentEntityId;
        NotifyParent(parentEntityId, catchable, true);
    }

    // 엔티티를 기존 부모에서 분리한다.
    public void HandleEntityPicked(CatchableObj catchable)
    {
        if (catchable == null) return;
        containerEjections.Remove(catchable.NetworkId);

        PanInteraction.ForgetToss(catchable.NetworkId);
        detachedThrowParents.Remove(catchable.NetworkId);

        NotifyParent(catchable.ParentEntityId, catchable, false);
        catchable.ParentEntityId = 0;
    }

    private void HandleCookProcessChanged(IReadOnlyList<CookProcessPacket> packets)
    {
        foreach (CookProcessPacket packet in packets)
        {
            if (!catchableDics.TryGetValue(packet.EntityId, out CatchableObj catchable))
                continue;

            if (catchable.TryGetComponent(out IngredientReaction reaction) &&
                !reaction.IsActionCompleted(IngredientAction.Cut))
                reaction.ApplyCutProgress(packet.Process);
        }
    }

    private void HandleEntityThrow(ReadOnlyMemory<byte> data)
    {
        EntityThrowPacket packet = MemoryPackSerializer.Deserialize<EntityThrowPacket>(data.Span);

        if (!catchableDics.TryGetValue(packet.EntityId, out CatchableObj catchable))
        {
            NetworkResponseWait.Error("Entity throw", packet.EntityId, "Target not found");
            PanInteraction.ForgetToss(packet.EntityId);
            return;
        }

        long previousParentId = catchable.ParentEntityId;
        if (TryResolveParent(previousParentId, out IEntityParentReceiver receiver) &&
            receiver is IEntityThrowParentReceiver throwReceiver)
        {
            throwReceiver.HandleEntityThrown(catchable);
        }
        else if (detachedThrowParents.TryGetValue(packet.EntityId, out IEntityThrowParentReceiver detachedParent))
        {
            if (detachedParent is not UnityEngine.Object obj || obj != null)
                detachedParent.HandleEntityThrown(catchable);
        }
        detachedThrowParents.Remove(packet.EntityId);

        NotifyParent(previousParentId, catchable, false);
        catchable.Holder?.Interact.ApplyThrown(catchable);
        catchable.ParentEntityId = 0;
        catchable.ApplyThrow(packet);
    }

    private void RemoveAndRelease(long entityId)
    {
        containerEjections.Remove(entityId);
        PanInteraction.ForgetToss(entityId);
        detachedThrowParents.Remove(entityId);
        IngredientEgg.HandleDestroyedEntity(entityId);
        pendingParentChanges.Remove(entityId);

        if (!catchableDics.TryGetValue(entityId, out CatchableObj catchable))
            return;

        catchable.IsRespawning = false;

        // Destroyed food must leave its container before pooling resets its identity.
        // Otherwise PlateInteraction retains a pooled food reference and a submission lock.
        if (TryResolveParent(catchable.ParentEntityId, out IEntityParentReceiver parent))
        {
            if (parent is TrashCanInteraction trashCan) trashCan.HandleDestroyedObj(catchable);
            parent.HandleEntityRemoved(catchable);
        }
        catchable.ParentEntityId = 0;

        catchableDics.Remove(entityId);
        ObjectPoolManager.Instance.activeObjDict.Remove(entityId);
        catchable.ReleaseCombinedVisual();

        ObjectPoolManager.Instance.Push(catchable.gameObject);
    }

    private void HandleToolSpawn(ReadOnlyMemory<byte> data)
    {
        if (StageReturnFlow.IsSettlementActive) return;
        ToolSpawnPacket packet = MemoryPackSerializer.Deserialize<ToolSpawnPacket>(data.Span)!;

        if (!Define.TryGetEntityCategory((Define.eToolId)packet.ToolId, out EntityCategory category))
        {
            Debug.LogError($"Unsupported spawned tool ID: {packet.ToolId}");
            return;
        }
        Vector3 pos = ProtocolTypeConverter.ToUnityVector3(packet.Position);
        Quaternion rot = Quaternion.identity;

        GameObject go = ObjectPoolManager.Instance.Pop(category.ToString(), pos, rot);
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
        obj.SetNetworkRouter(this);
        ApplyPendingParentChange(networkId, obj);
    }

    public void Remove(long networkId)
    {
        containerEjections.Remove(networkId);
        PanInteraction.ForgetToss(networkId);
        detachedThrowParents.Remove(networkId);
        pendingParentChanges.Remove(networkId);
        catchableDics.Remove(networkId);
    }
    public bool TryGet(long networkId, out CatchableObj obj)
    {
        return catchableDics.TryGetValue(networkId, out obj);
    }

    public void BeginContainerEjection(CatchableObj obj, long parentId)
    {
        containerEjections[obj.NetworkId] = parentId;
    }

    public void DetachForContainerEjection(CatchableObj obj)
    {
        NotifyParent(obj.ParentEntityId, obj, false);
        if (obj.Holder != null)
        {
            obj.Holder.Interact.TryReleaseHeld(obj);
            obj.OnDrop();
        }
        obj.ParentEntityId = 0;
    }

    public void EndContainerEjection(long entityId, long parentId)
    {
        if (containerEjections.TryGetValue(entityId, out long currentParent) && currentParent == parentId)
            containerEjections.Remove(entityId);
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
        if (parentEntityId == 0 || parentEntityId == -1) return;

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
        containerEjections.Remove(entityId);
        PanInteraction.ForgetToss(entityId);
        detachedThrowParents.Remove(entityId);
        pendingParentChanges.Remove(entityId);
        ReleaseNetworkBoundState(entityId);
        catchableDics.Remove(entityId);
        ObjectPoolManager.Instance.activeObjDict.Remove(entityId);
    }

    /// <summary>
    /// NetworkID가 교체되거나 오브젝트가 풀로 돌아가기 전에 이전 런타임 상태를 해제합니다.
    /// </summary>
    private static void ReleaseNetworkBoundState(long entityId)
    {
        if (entityId <= 0) return;

        if (ConveyorBeltRegistry.TryGetOwner(entityId, out ConveyorBeltController belt))
        {
            belt.UnregisterItem(entityId);
        }

    }

    private void Register(long entityId, CatchableObj catchable)
    {
        catchableDics[entityId] = catchable;
        catchable.SetNetworkRouter(this);
        ObjectPoolManager.Instance.activeObjDict[entityId] = catchable.gameObject;
        ApplyPendingParentChange(entityId, catchable);
    }

    // 등록 전 수신한 부모 변경 적용
    private void ApplyPendingParentChange(long entityId, CatchableObj catchable)
    {
        if (!pendingParentChanges.Remove(entityId, out long parentEntityId)) return;

        ApplyParentChange(catchable, parentEntityId);
    }
}
