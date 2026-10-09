using Protocol;
using Server;
using System.Collections.Generic;
using UnityEngine;

public class PrepInteraction : MapObjInteraction, IEntityParentReceiver, IPanPrimaryReceiver
{
    [SerializeField] private Transform itemSlot;
    [SerializeField] private ColliderRelay itemTrigger;

    private CatchableObj currentItem;
    private readonly ObjPlacement placement = new();
    private readonly HashSet<long> pendingEntities = new();

    private void OnEnable()
    {
        placement.Released += HandlePlacementReleased;
        if (itemTrigger == null) return;
        itemTrigger.TriggerEntered += HandleTriggerEnter;
        itemTrigger.TriggerExited += HandleTriggerExit;
    }

    private void OnDisable()
    {
        placement.Released -= HandlePlacementReleased;
        placement.Dispose();
        if (itemTrigger != null)
        {
            itemTrigger.TriggerEntered -= HandleTriggerEnter;
            itemTrigger.TriggerExited -= HandleTriggerExit;
        }

        pendingEntities.Clear();
        currentItem = null;
    }

    public bool TryReceivePanPrimary(PanInteraction pan, PlayerInteract player)
    {
        if (pan == null || player == null || itemSlot == null) return false;
        if (!IsRegistered || currentItem != null || pendingEntities.Count > 0) return false;
        if (pan.Catchable == null || pan.Catchable.NetworkId == 0) return false;

        pendingEntities.Add(pan.Catchable.NetworkId);
        player.RequestEntityInteract(NetworkId);
        return true;
    }

    private void HandleTriggerEnter(Collider other)
    {
        if (!IsRegistered || itemSlot == null || ServerManager.Instance == null) return;
        if (currentItem != null || pendingEntities.Count > 0) return;

        CatchableObj catchable = other.GetComponentInParent<CatchableObj>();
        if (catchable == null || catchable.Col != other) return;
        if (catchable.IsHold || !catchable.IsLocalOwner || catchable.NetworkId == 0) return;
        if (!pendingEntities.Add(catchable.NetworkId)) return;

        EntityInsertPacket packet = new()
        {
            SubjectEntityId = catchable.NetworkId,
            TargetEntityId = NetworkId
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void HandleTriggerExit(Collider other)
    {
        CatchableObj catchable = other.GetComponentInParent<CatchableObj>();
        if (catchable == null || catchable.Col != other) return;
        pendingEntities.Remove(catchable.NetworkId);

        if (currentItem != catchable) return;
        if (placement.IsPlaced) placement.Release();
    }

    private void HandlePlacementReleased(CatchableObj item)
    {
        if (currentItem != item || item.IsHold || !item.IsLocalOwner) return;
        if (item.ParentEntityId != NetworkId || item.NetworkId == 0) return;
        if (ServerManager.Instance == null) return;
        RequestDetach(item);
    }

    // 범위를 벗어난 아이템의 서버 보관 관계를 해제한다.
    private static void RequestDetach(CatchableObj item)
    {
        Vector3 velocity = item.Rb != null ? item.Rb.linearVelocity : Vector3.zero;
        EntityThrowPacket packet = new()
        {
            EntityId = item.NetworkId,
            Position = ProtocolTypeConverter.ToNumericsVector3(item.transform.position),
            Velocity = ProtocolTypeConverter.ToNumericsVector3(velocity)
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void HandleEntityAdded(CatchableObj entity)
    {
        if (entity == null) return;
        pendingEntities.Remove(entity.NetworkId);
        ApplyPut(entity);
    }

    public void HandleEntityRemoved(CatchableObj entity) => Release(entity);

    // 서버가 삽입을 승인하면 한 번만 보정하고 이후 물리 움직임을 유지한다.
    public void ApplyPut(CatchableObj catchable)
    {
        if (catchable == null || itemSlot == null || currentItem != null) return;
        if (!placement.TryPlace(catchable, itemSlot, alignColliderBottom: true))
        {
            Debug.LogError("[Prep] Failed to place the inserted item.", this);
            return;
        }
        currentItem = catchable;
    }

    public void Release(CatchableObj catchable)
    {
        if (currentItem != catchable) return;
        placement.Dispose();
        currentItem = null;
    }
}
