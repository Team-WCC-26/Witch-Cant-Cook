using UnityEngine;

public class StoveInteraction : MapObjInteraction, IHeldObjectReceiver, IPanPrimaryReceiver
{
    private PanInteraction currentPan;

    #region Unity Lifecycle

    private void OnTriggerEnter(Collider other)
    {
        if (currentPan != null) return; // pan exists
        if (!TryGetPan(other, out PanInteraction pan)) return; // pan not found
        CatchableObj panCatchable = pan.Catchable;
        if (panCatchable == null) return;
        if (panCatchable.Col != other) return;
        if (panCatchable.IsHold) return; // pan is being held

        PlacePan(pan);
    }

    private void OnTriggerExit(Collider other)
    {
        if (currentPan == null) return;

        CatchableObj panCatchable = currentPan.Catchable;
        if (panCatchable == null) return;
        if (panCatchable.Col != other) return;

        ReleasePan(currentPan);
    }

    #endregion

    #region Pan Placement

    [SerializeField] private Transform panSlot;

    // 직접 팬을 내려놓을 때
    public bool TryPlacePan(PanInteraction pan, PlayerInteract interact)
    {
        // Validate arguments
        if (pan == null) return false; 
        if (interact == null) return false; 

        if (currentPan != null) return false; // pan exists
        if (pan.Catchable != null && !interact.TryReleaseHeld(pan. Catchable)) return false; // Can't release

        PlacePan(pan);
        return true;
    }

    // 팬 위치를 화구 슬롯에 고정
    private void PlacePan(PanInteraction pan)
    {
        currentPan = pan;
        pan.PlaceOnStove(this, panSlot);
    }

    // 화구에서 팬을 분리한다.
    public void ReleasePan(PanInteraction pan)
    {
        if (currentPan != pan) return;

        currentPan = null;
        pan.ReleaseFromStove(this);
    }

    private bool TryGetPan(Collider other, out PanInteraction pan)
    {
        pan = other.GetComponentInParent<PanInteraction>();
        return pan != null;
    }

    #endregion

    #region Cooking

    [SerializeField] private float cookDuration = 2f;

    // 팬에 재료가 있으면 굽기를 시작한다.
    public void BeginCook(PanInteraction pan)
    {
        if (currentPan != pan) return; // 팬이 화구에 없으면 무시
        if (!pan.HasIngredient) return; // 팬에 재료가 없으면 무시 

        pan.StartGrill(cookDuration);
    }

    #endregion

    #region Interface Implementations

    // IPanPrimaryReceiver: 들고 있는 팬을 화구에 배치한다.
    public bool TryReceivePanPrimary(PanInteraction pan, PlayerInteract player)
    {
        return TryPlacePan(pan, player);
    }

    // IHeldObjectReceiver: 들고 있는 오브젝트가 팬이면 화구에 배치한다.
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

    #endregion
}
