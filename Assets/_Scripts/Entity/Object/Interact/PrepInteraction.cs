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
        if (currentItem != null) return false;
        if (pan.Catchable == null) return false;
        if (!player.TryReleaseHeld(pan.Catchable)) return false;

        TryAttachItem(pan.Catchable);
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.TryGetComponent(out CatchableObj catchable))
        {
            return;
        }
        if (catchable.IsHold) return;
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
    }

    public void HandleEntityAdded(CatchableObj entity)
    {
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

        TryAttachItem(catchable);
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