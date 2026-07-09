using UnityEngine;

public class StoveInteraction : MapObjInteraction, IHeldObjectReceiver
{
    [SerializeField] private Transform panSlot;
    [SerializeField] private float cookDuration = 2f;

    private PanInteraction currentPan;

    private void OnTriggerEnter(Collider other)
    {
        if (currentPan != null) return;
        if (!TryGetPan(other, out PanInteraction pan)) return;
        if (pan.Catchable != null && pan.Catchable.IsHold) return;

        PlacePan(pan);
    }

    private void OnTriggerExit(Collider other)
    {
        if (currentPan == null) return;
        if (!other.transform.IsChildOf(currentPan.transform)) return;

        Release(currentPan);
    }

    public bool TryReceiveHeldObject(CatchableObj heldObj, PlayerInteract interact)
    {
        if (heldObj == null) return false;
        if (interact == null) return false;
        if (currentPan != null) return false;
        if (!heldObj.TryGetComponent(out PanInteraction pan)) return false;
        if (!interact.TryReleaseHeld(heldObj)) return false;

        PlacePan(pan);
        return true;
    }

    public bool TryPlacePan(PanInteraction pan, PlayerInteract interact)
    {
        if (pan == null) return false;
        if (interact == null) return false;
        if (currentPan != null) return false;
        if (pan.Catchable != null && !interact.TryReleaseHeld(pan.Catchable)) return false;

        PlacePan(pan);
        return true;
    }

    public void BeginCook(PanInteraction pan)
    {
        if (currentPan != pan) return;
        if (!pan.HasIngredient) return;

        // The stove only starts cooking; the pan owns the ingredient state.
        pan.StartGrill(cookDuration);
    }

    public void Release(PanInteraction pan)
    {
        if (currentPan != pan) return;

        currentPan = null;
        pan.ReleaseFromStove(this);
    }

    private void PlacePan(PanInteraction pan)
    {
        currentPan = pan;
        pan.PlaceOnStove(this, panSlot);
    }

    private bool TryGetPan(Collider other, out PanInteraction pan)
    {
        pan = other.GetComponentInParent<PanInteraction>();
        return pan != null;
    }
}
