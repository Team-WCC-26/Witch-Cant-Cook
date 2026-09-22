using Protocol;
using Server;
using System.Collections.Generic;
using UnityEngine;

public class PrepInteraction : MapObjInteraction, IEntityParentReceiver, IPanPrimaryReceiver
{
    [SerializeField] private Transform itemSlot;
    [SerializeField] private Transform knifeSlot;

    private CatchableObj currentItem;
    private CatchableObj currentKnife;
    private readonly HashSet<long> pendingEntities = new();

    // 들고 있는 팬을 조리대의 아이템 슬롯에 배치한다.
    public bool TryReceivePanPrimary(PanInteraction pan, PlayerInteract player)
    {
        if (pan == null) return false;
        if (player == null) return false;
        if (!IsRegistered) return false;
        if (currentItem != null) return false;
        if (pan.Catchable == null) return false;
        if (pan.Catchable.NetworkId == 0) return false;
        if (!pendingEntities.Add(pan.Catchable.NetworkId)) return false;

        player.RequestEntityInteract(NetworkId);
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.TryGetComponent(out CatchableObj catchable))
        {
            return;
        }
        if (catchable.IsHold) return;
        if (currentItem == catchable || currentKnife == catchable) return;
        if (!IsRegistered) return;
        if (ServerManager.Instance == null) return;
        if (catchable.NetworkId == 0) return;
        if (!pendingEntities.Add(catchable.NetworkId)) return;

        // Insert request
        EntityInsertPacket packet = new()
        {
            SubjectEntityId = catchable.NetworkId,
            TargetEntityId = NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void OnCollisionExit(Collision collision)
    {
        if (!collision.gameObject.TryGetComponent(out CatchableObj catchable)) return;
        pendingEntities.Remove(catchable.NetworkId);

        if (currentItem != catchable) return;
        if (catchable.Category != EntityCategory.Pan) return;
        if (!catchable.IsLocalOwner) return;
        if (catchable.NetworkId == 0) return;
        if (ServerManager.Instance == null) return;

        RequestDetach(catchable);
    }

    // 조리대에서 벗어난 팬의 부모 해제를 서버에 요청한다.
    private static void RequestDetach(CatchableObj pan)
    {
        Vector3 velocity = pan.Rb != null
            ? pan.Rb.linearVelocity
            : Vector3.zero;

        EntityThrowPacket packet = new()
        {
            EntityId = pan.NetworkId,
            Position = ProtocolTypeConverter.ToNumericsVector3(pan.transform.position),
            Velocity = ProtocolTypeConverter.ToNumericsVector3(velocity)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void HandleEntityAdded(CatchableObj entity)
    {
        if (entity == null) return;

        // Parent result
        pendingEntities.Remove(entity.NetworkId);
        ApplyPut(entity);
    }

    public void HandleEntityRemoved(CatchableObj entity)
    {
        Release(entity);
    }

    public void ApplyPut(CatchableObj catchable)
    {
        if (catchable == null) return;

        if (catchable.Category == EntityCategory.Knife)
        {
            TryAttachKnife(catchable);
            return;
        }

        if (catchable.Category == EntityCategory.Pan &&
            catchable.TryGetComponent(out PanInteraction pan))
        {
            TryAttachPan(pan);
            return;
        }

        TryAttachItem(catchable);
    }

    // 팬을 아이템 슬롯에 동적 물리 상태로 배치한다.
    private void TryAttachPan(PanInteraction pan)
    {
        if (currentItem != null) return;

        currentItem = pan.Catchable;
        pan.PlaceOnPrep(itemSlot);
    }

    private void TryAttachItem(CatchableObj catchable)
    {
        if (currentItem != null) return;

        currentItem = catchable;
        AttachToSlot(catchable, itemSlot);
    }

    private void TryAttachKnife(CatchableObj knife)
    {
        if (currentKnife != null) return;

        currentKnife = knife;
        AttachToSlot(knife, knifeSlot);
    }

    private void AttachToSlot(CatchableObj catchable, Transform slot)
    {
        catchable.transform.position = slot.position;
        catchable.transform.rotation = slot.rotation;

        catchable.OnPlacedOnPrep(Release);
    }

    public void Release(CatchableObj catchable)
    {
        if (currentItem == catchable)
        {
            currentItem = null;
            return;
        }

        if (currentKnife == catchable)
        {
            currentKnife = null;
        }
    }
}
