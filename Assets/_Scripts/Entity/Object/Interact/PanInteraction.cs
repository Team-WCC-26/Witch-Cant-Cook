using System.Collections;
using Protocol;
using Server;
using UnityEngine;

public class PanInteraction : MonoBehaviour,
    IHeldPrimaryAction,
    IHeldObjectReceiver,
    IEntityParentReceiver,
    IEntityThrowParentReceiver,
    ICookReceiver
{
    [SerializeField] private CatchableObj catchable;

    private Collider itemTrigger;
    private IngredientReaction currentIngredient;
    private Vector3 currentIngredientOriginalScale;
    private StoveInteraction currentStove;
    private long pendingIngredientId;
    private long pendingTossId;
    private float pendingCookDuration = -1f;

    public CatchableObj Catchable => catchable;
    public bool HasIngredient => currentIngredient != null;

    #region Unity Lifecycle

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();

        if (itemTrigger == null)
        {
            foreach (Collider candidate in GetComponents<Collider>())
            {
                if (!candidate.isTrigger) continue;

                itemTrigger = candidate;
                break;
            }
        }
    }

    private void OnDisable()
    {
        RestorePanTriggerNow();
        HideCookGauge();
        pendingIngredientId = 0;
        pendingCookDuration = -1f;
        pendingTossId = 0;
        currentStove?.ReleasePan(this);
        currentStove = null;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 팬의 유효성 확인
        if (catchable == null || catchable.NetworkId == 0) return;

        if (currentIngredient != null) return; //중복 아이템 방지
        if (pendingIngredientId != 0) return; //이미 서버에 요청 중인 재료가 있음

        // 팬이 위를 향해야 함.
        if (Mathf.Abs(Mathf.DeltaAngle(0f, transform.eulerAngles.z)) > maxInsertAngle) return;

        // 아이템 유효성 확인
        if (!TryGetItem(other, out IngredientReaction _, out CatchableObj ingredientCatchable)) return;
        if (ingredientCatchable.NetworkId == 0) return;

        // 들고 있는 상태의 재료는 팬에 넣을 수 없음
        if (ingredientCatchable.IsHold) return;
        // 이 클라이언트가 마지막으로 던진 재료만 서버에 삽입 요청
        if (!ingredientCatchable.IsLocalOwner) return;


        pendingIngredientId = ingredientCatchable.NetworkId;
        RequestInsert(ingredientCatchable);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryGetItem(other, out _, out CatchableObj ingredientCatchable)) return;
        if (ingredientCatchable.NetworkId != pendingIngredientId) return;

        pendingIngredientId = 0;
    }

    #endregion

    #region Ingredient State

    [SerializeField] private Transform ingredientSlot;
    [SerializeField, Range(0f, 180f)] private float maxInsertAngle = 30f;

    // 팬에 재료를 고정하고 물리 상태를 비활성화한다.
    private void AttachItem(IngredientReaction ingredient, CatchableObj ingredientCatchable)
    {
        currentIngredient = ingredient;
        currentIngredientOriginalScale = ingredient.transform.localScale;

        Transform slot = ingredientSlot != null ? ingredientSlot : transform;
        ingredient.transform.SetParent(slot, false);
        ingredient.transform.localPosition = Vector3.zero;
        ingredient.transform.localRotation = Quaternion.identity;

        ingredientCatchable.ChangePickState(false);
        ingredientCatchable.SetPhysicsState(false);

        if (pendingCookDuration < 0f) return;

        ingredient.GaugeUI?.StartFill(pendingCookDuration);
        pendingCookDuration = -1f;
    }

    // 재료를 팬에서 분리하고 기존 크기를 복원한다.
    private void DetachItem(IngredientReaction ingredient)
    {
        ingredient.transform.SetParent(null, true);
        ingredient.transform.localScale = currentIngredientOriginalScale;
    }

    // Collider에서 팬에 넣을 수 있는 재료를 찾는다.
    private bool TryGetItem(Collider other, out IngredientReaction ingredient, out CatchableObj ingredientCatchable)
    {
        ingredient = other.GetComponentInParent<IngredientReaction>();
        ingredientCatchable = null;

        if (ingredient == null) return false;

        ingredientCatchable = ingredient.Catchable;
        if (ingredientCatchable == null) return false;
        if (ingredientCatchable.Category != EntityCategory.Ingredient) return false;

        return true;
    }

    // 재료를 팬에 넣어 달라고 서버에 요청한다.
    private void RequestInsert(CatchableObj ingredient)
    {
        EntityInsertPacket packet = new()
        {
            SubjectEntityId = ingredient.NetworkId,
            TargetEntityId = catchable.NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    // 팬에 든 음식을 빈 그릇으로 옮긴다.
    public bool TryMoveToPlate(PlateInteraction plate)
    {
        if (plate == null) return false; // 그릇이 없음
        if (!plate.IsEmpty) return false; // 빈 그릇이 아님
        if (currentIngredient == null) return false; // 팬에 재료가 없음

        IngredientReaction ingredient = currentIngredient;
        CatchableObj ingredientCatchable = ingredient.Catchable;
        if (ingredientCatchable == null) return false;

        HideCookGauge();
        currentIngredient = null;
        DetachItem(ingredient);
        plate.HandleEntityAdded(ingredientCatchable);
        return true;
    }

    #endregion

    #region Toss Item

    [Header("Toss Item")]
    [field: SerializeField, Min(0.01f)] private float tossForce = 3f;
    [field: SerializeField, Min(0.01f)] private float tossUpForce = 2f;
    [field: SerializeField, Min(0.01f)] private float triggerDisableDuration = 0.5f;
    [field: SerializeField, Min(0.01f)] private float damageImmuneDuration = 1f;

    private Coroutine triggerRestoreCoroutine;

    // 팬의 재료를 고정된 방향으로 배출해 달라고 서버에 요청한다.
    private void TossItem()
    {
        if (currentIngredient == null) return;
        if (pendingTossId != 0) return;

        CatchableObj ingredient = currentIngredient.Catchable;
        if (ingredient == null || ingredient.NetworkId == 0) return;
        if (ServerManager.Instance == null) return;

        Vector3 direction = catchable.Holder != null
            ? catchable.Holder.transform.forward
            : transform.forward;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;

        EntityThrowPacket packet = new()
        {
            EntityId = ingredient.NetworkId,
            Position = ProtocolTypeConverter.ToNumericsVector3(ingredient.transform.position),
            Velocity = ProtocolTypeConverter.ToNumericsVector3(
                direction * tossForce + Vector3.up * tossUpForce)
        };

        pendingTossId = ingredient.NetworkId;
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void DisablePanTrigger()
    {
        if (itemTrigger == null) return;

        if (triggerRestoreCoroutine != null)
            StopCoroutine(triggerRestoreCoroutine);

        itemTrigger.enabled = false;
        triggerRestoreCoroutine = StartCoroutine(RestorePanTriggerRoutine());
    }

    private IEnumerator RestorePanTriggerRoutine()
    {
        yield return new WaitForSeconds(triggerDisableDuration);
        itemTrigger.enabled = true;
        triggerRestoreCoroutine = null;
    }
    private void RestorePanTriggerNow()
    {
        if (triggerRestoreCoroutine != null)
        {
            StopCoroutine(triggerRestoreCoroutine);
            triggerRestoreCoroutine = null;
        }

        if (itemTrigger != null)
            itemTrigger.enabled = true;
    }

    #endregion

    #region Pan Placement

    // 팬을 화구 슬롯에 배치하고 조리를 시작한다.
    public void PlaceOnStove(StoveInteraction stove, Transform slot)
    {
        currentStove?.ReleasePan(this);
        currentStove = stove;

        AttachPan(slot);
    }

    // 현재 화구에서 팬을 분리하고 조리를 중단한다.
    public void ReleaseFromStove(StoveInteraction stove)
    {
        if (currentStove != stove) return;

        currentStove = null;
    }

    // 팬을 조리대 슬롯에 배치하고 물리 움직임을 유지한다.
    public void PlaceOnPrep(Transform slot)
    {
        AttachPan(slot);
    }

    // 현재 재료의 조리 게이지를 숨긴다.
    private void HideCookGauge()
    {
        currentIngredient?.GaugeUI?.Hide();
    }

    // 팬을 슬롯 위치로 보정하고 물리 움직임을 활성화한다.
    private void AttachPan(Transform slot)
    {
        if (catchable == null) return;
        if (slot == null) return;

        transform.position = slot.position;
        transform.rotation = slot.rotation;

        if (catchable.IsHold)
            catchable.OnDrop();
        else
            catchable.SetPhysicsState(true);

        if (catchable.Rb == null) return;

        catchable.Rb.linearVelocity = Vector3.zero;
        catchable.Rb.angularVelocity = Vector3.zero;
    }

    #endregion

    #region Interface Implementations

    // IHeldPrimaryAction: 선택된 대상에 팬 좌클릭 처리 위임
    public bool TryUsePrimary(PlayerInteract interact)
    {
        if (interact == null) return false;

        // 플레이어가 바라보는 대상 유무
        IInteractTarget target = interact.FindInteractTarget();

        // 대상이 팬을 받을 수 있는지 확인
        IPanPrimaryReceiver receiver = target.GetTargetComponent<IPanPrimaryReceiver>();
        if (receiver != null && receiver.TryReceivePanPrimary(this, interact))
            return true;

        // 팬에 재료가 있으면 팬에서 재료를 튕겨낸다.
        if (currentIngredient != null) TossItem();
        //TODO : 팬 휘두르기 모션

        return true;
    }

    // IHeldObjectReceiver: 플레이어가 들고 있는 재료 팬에 넣기
    public bool TryReceiveHeldObject(CatchableObj heldObj, PlayerInteract interact)
    {
        //함수 인자 검증
        if (heldObj == null) return false;
        if (interact == null) return false;

        if (currentIngredient != null) return false; //팬에 이미 재료가 있음
        if (pendingIngredientId != 0) return false; //이미 서버에 요청 중인 재료가 있음
        if (!heldObj.TryGetComponent(out IngredientReaction _)) return false; //재료가 아님
        if (heldObj.NetworkId == 0) return false;
        if (catchable == null || catchable.NetworkId == 0) return false;

        pendingIngredientId = heldObj.NetworkId;
        interact.RequestEntityInteract(catchable.NetworkId);
        return true;
    }

    // IEntityParentReceiver: 서버가 팬에 추가한 재료를 부착한다.
    public void HandleEntityAdded(CatchableObj entity)
    {
        // Validate argument
        if (entity == null) return;

        if (currentIngredient != null)
        {
            Debug.LogError("[Client Sync Error] Pan already contains an ingredient.");
            return;
        }

        if (!entity.TryGetComponent(out IngredientReaction ingredient)) return;

        pendingIngredientId = 0;
        AttachItem(ingredient, entity);
    }

    // IEntityParentReceiver: 서버가 팬에서 제거한 재료를 분리한다.
    public void HandleEntityRemoved(CatchableObj entity)
    {
        // Validate argument
        if (entity == null) return;

        if (currentIngredient == null) return;
        if (currentIngredient.Catchable != entity)
        {
            Debug.LogError("[Client Sync Error] Removed item does not match the item in the pan.");
            return;
        }

        pendingIngredientId = 0;
        pendingCookDuration = -1f;
        HideCookGauge();
        IngredientReaction ingredient = currentIngredient;
        currentIngredient = null;
        DetachItem(ingredient);
    }

    // IEntityThrowParentReceiver: 팬에서 배출된 재료의 예외 처리를 적용한다.
    public void HandleEntityThrown(CatchableObj entity)
    {
        if (entity == null) return;
        if (currentIngredient == null || currentIngredient.Catchable != entity) return;

        pendingTossId = 0;
        DisablePanTrigger();

        if (entity.TryGetComponent(out IngredientDamageController damageController))
            damageController.DisableDamageFor(damageImmuneDuration);
    }

    // ICookReceiver: 서버가 전달한 남은 시간으로 조리 게이지를 시작한다.
    public void HandleCookStart(CookStartPacket packet)
    {
        float duration = packet.CookingTimeMs / 1000f;
        if (currentIngredient == null)
        {
            pendingCookDuration = duration;
            return;
        }

        currentIngredient.GaugeUI?.StartFill(duration);
    }

    // ICookReceiver: 서버의 조리 일시정지를 반영한다.
    public void HandleCookPause(CookPausePacket packet)
    {
        pendingCookDuration = -1f;
        HideCookGauge();
    }

    // ICookReceiver: 서버의 굽기 완료를 반영한다.
    public void HandleCookComplete(CookCompletePacket packet)
    {
        if ((packet.CookType & IngredientState.Grilled) == 0) return;

        pendingCookDuration = -1f;
        HideCookGauge();
    }

    #endregion
}
