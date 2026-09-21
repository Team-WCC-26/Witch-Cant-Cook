using Protocol;
using Server;
using UnityEngine;

public class StoveInteraction : MapObjInteraction, IHeldObjectReceiver, IPanPrimaryReceiver, IEntityParentReceiver
{
    private PanInteraction currentPan;
    private long pendingPanId;

    #region Unity Lifecycle

    private void OnTriggerEnter(Collider other)
    {
        //validate stove
        if (!IsRegistered) return;

        if (currentPan != null) return; // pan exists
        if (pendingPanId != 0) return; // pan is pending

        // Validate pan
        if (!TryGetPan(other, out PanInteraction pan)) return;
        CatchableObj panCatchable = pan.Catchable;
        if (panCatchable == null) return;
        if (panCatchable.NetworkId == 0) return;
        if (panCatchable.Col != other) return;

        // 들고 있는 상태의 팬은 충돌로 배치하지 않음
        if (panCatchable.IsHold) return;
        // 이 클라이언트가 마지막으로 놓거나 던진 팬만 서버에 삽입 요청
        if (!panCatchable.IsLocalOwner) return;

        pendingPanId = panCatchable.NetworkId;
        RequestInsert(panCatchable);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryGetPan(other, out PanInteraction pan)) return;
        if (pan.Catchable == null) return;
        if (pan.Catchable.NetworkId != pendingPanId) return;

        pendingPanId = 0;
    }

    #endregion

    #region Pan Placement

    [SerializeField] private Transform panSlot;

    // 들고 있는 팬을 화구에 놓아 달라고 서버에 요청한다.
    public bool TryPlacePan(PanInteraction pan, PlayerInteract interact)
    {
        if (!IsRegistered) return false;

        // Validate arguments
        if (pan == null) return false; 
        if (interact == null) return false; 

        if (currentPan != null) return false; // pan exists
        if (pan.Catchable == null || pan.Catchable.NetworkId == 0) return false; // pan is not registered

        interact.RequestEntityInteract(NetworkId);
        return true;
    }

    // 물리적으로 들어온 팬을 화구에 넣어 달라고 서버에 요청한다.
    private void RequestInsert(CatchableObj pan)
    {
        EntityInsertPacket packet = new()
        {
            SubjectEntityId = pan.NetworkId,
            TargetEntityId = NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
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
        if (!heldObj.TryGetComponent(out PanInteraction _)) return false;
        if (!IsRegistered) return false;

        interact.RequestEntityInteract(NetworkId);
        return true;
    }

    // IEntityParentReceiver: 서버가 화구에 추가한 팬을 배치한다.
    public void HandleEntityAdded(CatchableObj entity)
    {
        if (currentPan != null)
        {
            Debug.LogError("[Client Sync Error] Stove already contains a pan.");
            return;
        }

        //validate argument
        if (entity == null) return;
        if (!entity.TryGetComponent(out PanInteraction pan)) return;

        pendingPanId = 0;
        PlacePan(pan);
    }

    // IEntityParentReceiver: 서버가 화구에서 제거한 팬을 분리한다.
    public void HandleEntityRemoved(CatchableObj entity)
    {
        // Validate argument
        if (entity == null) return;

        if (currentPan == null) return;
        if (currentPan.Catchable != entity)
        {
            Debug.LogError("[Client Sync Error] Stove already contains another pan.");
            return;
        }

        pendingPanId = 0;
        ReleasePan(currentPan);
    }

    #endregion
}
