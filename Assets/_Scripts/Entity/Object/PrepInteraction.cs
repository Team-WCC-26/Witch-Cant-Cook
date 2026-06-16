using UnityEngine;

public class PrepInteraction : MapObjInteraction
{
    [SerializeField] private Transform itemSlot;
    [SerializeField] private Transform knifeSlot;

    private CatchableObj currentItem;
    private CatchableObj currentKnife;

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.TryGetComponent(out CatchableObj catchable))
        {
            return;
        }
        if (catchable.IsHold) return;
        if (!IsRegistered) return;

        Router?.RequestPut(this, catchable);
    }

    public void ApplyPut(CatchableObj catchable)
    {
        if (catchable == null) return;

        if (catchable.ObjType == CatchableObjType.Knife)
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