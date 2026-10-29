using System.Collections;
using System.Collections.Generic;
using Protocol;
using Server;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

public enum PotState
{
    Idle,
    Cooking,
    Ejecting
}

public class PotInteraction : MapObjInteraction,
    IPunchReceiver,
    IEntityParentReceiver,
    ICookReceiver,
    IPotNetworkReceiver,
    IClientStageResettable
{
    #region Inspecter

    [Header("Storage")]
    [SerializeField] private Transform parentRecipe;
    [SerializeField] private Transform parentETC;

    [Header("Cooking")]
    [SerializeField] private InteractionGaugeUI gaugeUI;
    [SerializeField] private Animator cookingVisualAnimator;
    [SerializeField] private GameObject ingredientIcons;
    [SerializeField] private Image[] ingredientSlots;
    [SerializeField] private GameObject potIcon;
    private const int MaxIngredientIcons = 4;
    private int iconVersion;

    [Header("솥 시점")]
    [SerializeField] private CinemachineCamera potCamera;
    [SerializeField] private int potCameraPriority = 20;
    private PrioritySettings previousCameraPriority;
    private bool isPotCameraActive;
    private PlayerInputHandler trappedLocalInput;
    private bool previousInputBlocked;

    [Header("조리 완료 파티클")]
    [SerializeField, InspectorName("파티클 프리팹")]
    private DishCompleteVfx cookCompleteEffectPrefab;
    [SerializeField, InspectorName("재생 위치 (비워두면 솥 윗면)")]
    private Transform cookCompleteEffectOrigin;
    [SerializeField, InspectorName("위치 보정")]
    private Vector3 cookCompleteEffectOffset = new Vector3(0f, 0.15f, 0f);
    [SerializeField, Min(0.1f)]
    [Tooltip("입자 크기는 유지하고 퍼지는 범위와 확산 고리 크기만 조절합니다. 1은 기본, 2는 두 배입니다.")]
    private float cookCompleteEffectSpread = 2f;

    #endregion

    private static readonly int IsCookingHash = Animator.StringToHash("IsCooking");
    private static readonly int CookingRattleHash = Animator.StringToHash("CookingRattle");
    private static readonly int PunchRattleHash = Animator.StringToHash("PunchRattle");
    private static readonly int EjectBounceHash = Animator.StringToHash("EjectBounce");
    private static readonly int IngredientIntakeHash = Animator.StringToHash("IngredientIntake");
    private static readonly int CookingIdleHash = Animator.StringToHash("Idle");
    private const float PunchRattleDuration = 0.55f;
    private const float IngredientIntakeDuration = 0.62f;
    // 0.78초 클립과 짧은 전환이 끝날 때까지 다음 배출 연출을 대기시킨다.
    private const float EjectBounceDuration = 0.85f;
    private bool cookingVisualActive;
    private float visualOverrideUntilTime;
    private int visualOverrideStateHash;

    private PotState state = PotState.Idle;
    public PotState State => state;

    // Object storage
    private readonly HashSet<CatchableObj> pendingObjects = new(); // Awaiting server response
    private readonly HashSet<CatchableObj> storedObjects = new(); // Total Input
    private readonly List<CatchableObj> recipeObjects = new(); // During Idle State
    private readonly List<CatchableObj> etcObjects = new(); // During Cooking State
    private readonly List<CatchableObj> postEjectObjects = new(); // During Ejecting
    private CatchableObj cookingBowl; // Cooking Trigger

    // Player entry
    private readonly List<PlayerBrain> trappedPlayers = new();
    private readonly HashSet<string> pendingPlayerEntries = new(); // Awaiting server response
    private bool playerEnteredDuringEjection;
    private const float EjectionClearance = 0.05f;
    private sealed class PlayerEjectionCollision
    {
        public PlayerBrain player;
        public Collider[] potColliders;
        public readonly List<(Collider body, Collider pot)> ignoredPairs = new();
    }
    private readonly List<PlayerEjectionCollision> playerEjectionCollisions = new();

    // Network 
    private ObjectNetworkRouter objectRouter;
    private PotCookCompletePacket pendingCookCompletePacket;
    private float? pendingCookingDuration;
    private long lastCookCompleteEffectDishId;

    #region Unity Callbacks

    // 솥 활성화 초기화
    private void OnEnable()
    {
        ResetPotData();
    }

    private void FixedUpdate()
    {
        for (int index = playerEjectionCollisions.Count - 1; index >= 0; index--)
        {
            PlayerEjectionCollision collision = playerEjectionCollisions[index];
            PlayerBrain player = collision.player;
            bool inactive = player == null || !player.isActiveAndEnabled;
            if (!inactive)
            {
                if (player.ActionController.IsRagdollLaunchPending || !IsPlayerClearOfPot(collision)) continue;
            }
            RestorePlayerEjectionCollisions(collision);
            playerEjectionCollisions.RemoveAt(index);
        }
    }

    // 게이지가 실제 표시되는 동안에만 시각용 솥 애니메이션을 재생한다.
    private void LateUpdate()
    {
        if (Time.time < visualOverrideUntilTime) return;

        bool shouldPlay = state == PotState.Cooking &&
            gaugeUI != null && gaugeUI.gameObject.activeInHierarchy;
        if (cookingVisualAnimator == null || shouldPlay == cookingVisualActive) return;

        cookingVisualActive = shouldPlay;
        cookingVisualAnimator.SetBool(IsCookingHash, shouldPlay);
        if (shouldPlay)
            cookingVisualAnimator.Play(CookingRattleHash, 0, Random.value);
    }

    // 솥 보관물 반환 및 초기화
    private void OnDisable()
    {
        StopPotCoroutines();
        ClearObjects();
        ResetPotData();
    }

    // 진입 대상 서버 요청
    private void OnTriggerEnter(Collider other)
    {
        if (TryGetPlayer(other, out PlayerBrain player))
        {
            RequestPlayerEntry(player);
            return;
        }

        CatchableObj catchable = other.GetComponentInParent<CatchableObj>();
        if (catchable == null) return;
        if (storedObjects.Contains(catchable) || !pendingObjects.Add(catchable)) return;

        RequestObjEntry(catchable);
    }

    // 선택 시 배출 기준 방향과 랜덤 범위 표시
    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        Ejector.DrawGizmos();
#endif
    }

    #endregion

    #region Lifecycle

    // 솥 패킷에서 오브젝트를 찾을 라우터 연결
    public override void InitializeRouter(MapObjNetworkRouter router)
    {
        base.InitializeRouter(router);
        objectRouter = router != null ? router.GetComponent<ObjectNetworkRouter>() : null;
    }

    // 솥 데이터 초기화
    public void ResetClientStage()
    {
        StopPotCoroutines();
        ResetPotData();
    }

    private void ResetPotData()
    {
        foreach (PlayerEjectionCollision collision in playerEjectionCollisions)
            RestorePlayerEjectionCollisions(collision);
        playerEjectionCollisions.Clear();
        foreach (PlayerBrain player in trappedPlayers)
        {
            if (player != null && player.Health != null)
                player.Health.IsDmgBlocked = false;
            PlayerGlow.SetActive(player, false);
        }
        RestorePlayerInput();
        RestorePlayerCamera();
        state = PotState.Idle;
        lastCookCompleteEffectDishId = 0;

        ClearIngredientIcons();

        // Object storage
        pendingObjects.Clear();
        storedObjects.Clear();
        recipeObjects.Clear();
        etcObjects.Clear();
        postEjectObjects.Clear();
        cookingBowl = null;

        // Player entry
        trappedPlayers.Clear();
        pendingPlayerEntries.Clear();
        playerEnteredDuringEjection = false;

        // Network
        pendingCookCompletePacket = null;
        pendingCookingDuration = null;

        // Ejection
        ejector?.Reset();

        HideCookingUI();
        cookingVisualActive = false;
        visualOverrideUntilTime = 0f;
        visualOverrideStateHash = 0;
        if (cookingVisualAnimator != null)
        {
            cookingVisualAnimator.SetBool(IsCookingHash, false);
            cookingVisualAnimator.Play(CookingIdleHash, 0, 0f);
        }
    }

    // 실행 중 솥 코루틴 중단
    private void StopPotCoroutines()
    {
        ejector?.Reset();
    }

    // 보관 오브젝트 Pool 반환
    private void ClearObjects()
    {
        if (ObjectPoolManager.Instance == null) return;

        foreach (CatchableObj catchable in storedObjects)
        {
            if (catchable == null) continue;
            ObjectPoolManager.Instance.Push(catchable.gameObject);
        }
    }

    #endregion

    #region Server

    // 솥 진입 패킷 전송
    private void RequestObjEntry(CatchableObj catchable)
    {
        if (catchable == null) return;
        if (NetworkId == 0 || catchable.NetworkId == 0 || ServerManager.Instance == null)
        {
            pendingObjects.Remove(catchable);
            return;
        }

        EntityInsertPacket packet = new()
        {
            SubjectEntityId = catchable.NetworkId,
            TargetEntityId = NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    // 솥 진입 결과 수신
    public void ReceiveObjEntry(CatchableObj catchable, PotState entryState)
    {
        if (catchable == null) return;

        pendingObjects.Remove(catchable);
        if (storedObjects.Contains(catchable)) return;

        if (entryState == PotState.Idle)
            HandleIdleEntry(catchable);
        else
        {
            StoreETC(catchable);
            if (entryState == PotState.Ejecting && storedObjects.Contains(catchable))
                postEjectObjects.Add(catchable);
        }

        // 재료, 그릇 등 서버가 수락해 보관한 모든 오브젝트에 같은 연출을 적용한다.
        if (entryState != PotState.Ejecting && storedObjects.Contains(catchable))
            PlayVisualOverride(IngredientIntakeHash, IngredientIntakeDuration, 0.04f);
    }

    // 솥 조리 시작 결과 수신
    public void ReceiveCookStarted(CatchableObj bowl, float duration)
    {
        if (state != PotState.Idle) return;
        if (bowl == null || bowl != cookingBowl) return;
        if (recipeObjects.Count == 0) return;

        ChangeState(PotState.Cooking);
        ClearIngredientIcons();
        SetGaugePartsActive(true);
        if (potIcon != null) potIcon.SetActive(true);
        gaugeUI?.StartFill(duration, HideCookingUI);
    }

    // 강제 배출 패킷 전송
    private void RequestForceEject()
    {
        if (NetworkId == 0 || ServerManager.Instance == null) return;

        PotForceEjectPacket packet = new()
        {
            PotEntityId = NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    // 재료와 그릇 수신 후 대기 중인 조리 시작
    private void TryStartCooking()
    {
        if (!pendingCookingDuration.HasValue) return;
        if (cookingBowl == null || recipeObjects.Count == 0) return;

        float duration = pendingCookingDuration.Value;
        pendingCookingDuration = null;
        ReceiveCookStarted(cookingBowl, duration);
    }

    // 서버가 소비한 조리 재료를 솥 보관 목록에서 제거
    private void RemoveConsumedObj()
    {
        CatchableObj[] consumedObjects = recipeObjects.ToArray();
        recipeObjects.Clear();
        RefreshIngredientIcons();

        foreach (CatchableObj catchable in consumedObjects)
        {
            if (catchable == null) continue;

            pendingObjects.Remove(catchable);
            storedObjects.Remove(catchable);
            postEjectObjects.Remove(catchable);
        }
    }

    #endregion

    #region Ingredient UI

    private void ClearIngredientIcons()
    {
        iconVersion++;
        if (ingredientIcons == null) return;
        ingredientIcons.SetActive(false);
        foreach (Transform child in ingredientIcons.GetComponentsInChildren<Transform>(true))
        {
            if (child != ingredientIcons.transform) child.gameObject.SetActive(false);
        }
        if (ingredientSlots == null) return;
        foreach (Image slot in ingredientSlots)
        {
            if (slot == null) continue;
            slot.sprite = null;
            slot.enabled = false;
        }
    }

    private void RefreshIngredientIcons()
    {
        ClearIngredientIcons();
        if (state != PotState.Cooking) HideCookingUI();
        if (!isActiveAndEnabled || state != PotState.Idle || recipeObjects.Count == 0 ||
            ingredientIcons == null || ingredientSlots == null) return;

        HideCookingUI();
        SetGaugePartsActive(false);
        // The existing gauge canvas also owns the ingredient row.
        if (gaugeUI != null) gaugeUI.gameObject.SetActive(true);
        ingredientIcons.SetActive(true);
        int version = iconVersion;
        int count = Mathf.Min(recipeObjects.Count, Mathf.Min(MaxIngredientIcons, ingredientSlots.Length));
        for (int index = 0; index < count; index++)
        {
            CatchableObj ingredient = recipeObjects[index];
            Image slot = ingredientSlots[index];
            if (slot == null || ingredient == null) continue;
            slot.gameObject.SetActive(true);
            string address = (ingredient.Data as Ingredient)?.IconName;
            SpriteCache.Assign(slot, address, () => this != null && isActiveAndEnabled &&
                iconVersion == version && state == PotState.Idle);
        }
    }

    private void HideCookingUI()
    {
        if (potIcon != null) potIcon.SetActive(false);
        gaugeUI?.Hide();
    }

    private void SetGaugePartsActive(bool active)
    {
        if (gaugeUI == null) return;
        Transform background = gaugeUI.transform.Find("BG");
        Transform fill = gaugeUI.transform.Find("Fill");
        if (background != null) background.gameObject.SetActive(active);
        if (fill != null) fill.gameObject.SetActive(active);
    }

    #endregion

    #region Object Storage

    // Idle 상태 진입 오브젝트 분류
    private void HandleIdleEntry(CatchableObj catchable)
    {
        if (catchable.TryGetComponent(out IngredientReaction _))
        {
            StoreRecipe(catchable);
            return;
        }

        if (catchable.TryGetComponent(out PlateInteraction plate) && plate.IsEmpty)
        {
            if (recipeObjects.Count == 0 && !pendingCookingDuration.HasValue)
            {
                StoreETC(catchable);
                return;
            }

            StoreCookingBowl(catchable);
            return;
        }

        StoreETC(catchable);
    }

    // 조리 대상 재료 보관
    private void StoreRecipe(CatchableObj catchable)
    {
        if (!TryStoreObject(catchable, parentRecipe)) return;

        recipeObjects.Add(catchable);
        RefreshIngredientIcons();
    }

    // 조리 시작 그릇 보관
    private void StoreCookingBowl(CatchableObj bowl)
    {
        if (!TryStoreObject(bowl, parentRecipe)) return;
        cookingBowl = bowl;
    }

    // 배출 대상 오브젝트 보관
    private void StoreETC(CatchableObj catchable)
    {
        if (!TryStoreObject(catchable, parentETC)) return;

        etcObjects.Add(catchable);
    }

    // 오브젝트 비활성 보관
    private bool TryStoreObject(CatchableObj catchable, Transform storageParent)
    {
        if (storageParent == null)
        {
            Debug.LogError("Pot storage parent is not assigned.");
            pendingObjects.Remove(catchable);
            return false;
        }

        if (!storedObjects.Add(catchable)) return false;

        catchable.ChangePickState(false);
        catchable.SetPhysicsState(false);
        catchable.transform.SetParent(storageParent, false);
        catchable.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        catchable.gameObject.SetActive(false);
        return true;
    }

    #endregion

    #region Ejection

    [Header("Ejection")]
    [SerializeField] private Transform ejectPoint;
    [SerializeField, Min(0f)] private float ejectInterval = 0.15f;
    [SerializeField] private Vector3 ejectBaseDirection = new(0f, 1f, 1f);
    [SerializeField, Min(0f)] private float ejectRandomRangeX = 0.3f;
    [SerializeField] private float ejectRandomRangeYMin = -0.05f;
    [SerializeField] private float ejectRandomRangeYMax = 0.2f;
    [SerializeField, Min(0f)] private float ejectRandomRangeZ = 0.3f;
    [SerializeField, Min(0f)] private float ejectForce = 5f;
    [SerializeField, Min(0f)] private float bowlEjectForce = 4f;
    [SerializeField, Min(0f)] private float playerEjectForce = 8f;
    [SerializeField, Min(0.1f)] private float ejectGizmoLength = 2f;

    private PotEjector ejector;
    private PotEjector Ejector => ejector ??= new PotEjector(this);

    internal bool HasCompletedDish => pendingCookCompletePacket != null;
    internal long CompletedDishId => pendingCookCompletePacket?.DishEntityId ?? 0;
    internal float PlayerEjectForce => playerEjectForce;

    // 직렬화 필드는 그대로 두어 기존 프리팹/씬의 설정을 유지한다.
    internal void ConfigureEjection(ObjEjector.Settings settings)
    {
        settings.ejectPoint = ejectPoint;
        settings.interval = ejectInterval;
        settings.baseDirection = ejectBaseDirection;
        settings.randomRangeX = ejectRandomRangeX;
        settings.randomRangeYMin = ejectRandomRangeYMin;
        settings.randomRangeYMax = ejectRandomRangeYMax;
        settings.randomRangeZ = ejectRandomRangeZ;
        settings.force = ejectForce;
        settings.gizmoLength = ejectGizmoLength;
    }

    internal CatchableObj FindEjectObj(long entityId)
    {
        CatchableObj catchable = null;
        objectRouter?.TryGet(entityId, out catchable);
        return catchable;
    }

    internal void BeginEjection()
    {
        pendingCookingDuration = null;
        HideCookingUI();
        ChangeState(PotState.Ejecting);
    }

    internal IEnumerator WaitForEjectionVisual(bool waitForPunchRattle)
    {
        if (waitForPunchRattle &&
            (visualOverrideStateHash != PunchRattleHash || Time.time >= visualOverrideUntilTime))
            PlayVisualOverride(PunchRattleHash, PunchRattleDuration);

        while (Time.time < visualOverrideUntilTime)
            yield return null;
    }

    internal float PlayEjectionVisual()
    {
        if (cookingVisualAnimator == null) return 0f;
        PlayVisualOverride(EjectBounceHash, EjectBounceDuration, 0.05f);
        return EjectBounceDuration;
    }

    internal float GetObjEjectForce(CatchableObj catchable)
    {
        return pendingCookCompletePacket != null &&
            catchable.NetworkId == pendingCookCompletePacket.DishEntityId
            ? bowlEjectForce : ejectForce;
    }

    internal void PrepareObjEjection(CatchableObj catchable)
    {
        RemoveStoredObject(catchable);
        pendingObjects.Remove(catchable);
    }

    private void PlayVisualOverride(int stateHash, float duration, float blendDuration = 0f)
    {
        if (cookingVisualAnimator == null) return;

        visualOverrideUntilTime = Time.time + duration;
        visualOverrideStateHash = stateHash;
        cookingVisualActive = false;
        cookingVisualAnimator.SetBool(IsCookingHash, false);
        if (blendDuration > 0f)
            cookingVisualAnimator.CrossFadeInFixedTime(stateHash, blendDuration, 0, 0f);
        else
            cookingVisualAnimator.Play(stateHash, 0, 0f);
    }

    // 보관 목록에서 오브젝트 제거
    private void RemoveStoredObject(CatchableObj catchable)
    {
        if (catchable == null) return;

        storedObjects.Remove(catchable);
        recipeObjects.Remove(catchable);
        etcObjects.Remove(catchable);
        postEjectObjects.Remove(catchable);

        if (cookingBowl == catchable)
            cookingBowl = null;
        RefreshIngredientIcons();
    }

    // 현재 배출 완료 및 신규 진입 보존
    internal void FinishEjection()
    {
        CatchableObj[] enteredDuringEjection = postEjectObjects.ToArray();
        postEjectObjects.Clear();
        ChangeState(PotState.Idle);
        bool swallowedAfterEjection = playerEnteredDuringEjection;
        playerEnteredDuringEjection = false;

        foreach (CatchableObj catchable in enteredDuringEjection)
        {
            if (catchable == null || !storedObjects.Contains(catchable)) continue;

            storedObjects.Remove(catchable);
            recipeObjects.Remove(catchable);
            etcObjects.Remove(catchable);

            HandleIdleEntry(catchable);
            swallowedAfterEjection |= storedObjects.Contains(catchable);
        }

        if (swallowedAfterEjection)
            PlayVisualOverride(IngredientIntakeHash, IngredientIntakeDuration, 0.04f);

        TryStartCooking();
        pendingCookCompletePacket = null;
    }

    // Inspector 디버그 강제 배출
    public void DebugForceEject()
    {
        if (Ejector.IsEjecting) return;

        PotEjectPacket packet = new()
        {
            PotEntityId = NetworkId,
            EjectSeed = System.Environment.TickCount,
            EjectDelayMs = 0
        };

        foreach (PlayerBrain player in trappedPlayers)
        {
            if (player != null) packet.PlayerIds.Add(player.PlayerId);
        }

        foreach (CatchableObj catchable in recipeObjects)
        {
            if (catchable != null) packet.EntityIds.Add(catchable.NetworkId);
        }

        if (cookingBowl != null)
            packet.EntityIds.Add(cookingBowl.NetworkId);

        foreach (CatchableObj catchable in etcObjects)
        {
            if (catchable != null && catchable != cookingBowl)
                packet.EntityIds.Add(catchable.NetworkId);
        }

        Ejector.Enqueue(packet);
    }

    #endregion

    #region Player

    // 로컬 플레이어 솥 진입 전송
    private void RequestPlayerEntry(PlayerBrain player)
    {
        if (player == null || PlayerSpawnManager.Instance == null) return;
        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        if (trappedPlayers.Contains(player)) return;
        foreach (PlayerEjectionCollision collision in playerEjectionCollisions)
        {
            if (collision.player == player) return;
        }
        if (!pendingPlayerEntries.Add(player.PlayerId)) return;
        if (NetworkId == 0 || ServerManager.Instance == null)
        {
            pendingPlayerEntries.Remove(player.PlayerId);
            return;
        }

        ToolPlayerEnterPacket packet = new()
        {
            ToolEntityId = NetworkId,
            PlayerId = player.PlayerId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    // 서버 큐에서 꺼낸 플레이어 배출
    internal void PreparePlayerEjection(PlayerBrain player)
    {
        if (player == null) return;

        IgnorePlayerEjectionCollisions(player);

        if (player.Health != null)
            player.Health.IsDmgBlocked = false;
        PlayerGlow.SetActive(player, false);
        trappedPlayers.Remove(player);
        pendingPlayerEntries.Remove(player.PlayerId);
        if (player.Interact?.HeldObj != null)
        {
            CatchableObj heldObject = player.Interact.HeldObj;
            player.Interact.ForceDropHeld();

            if (player.Interact.TryReleaseHeld(heldObject))
                heldObject.OnDrop();
        }

        // Only the owning client applies physics; peers receive its movement snapshots.
        if (PlayerSpawnManager.Instance == null ||
            !PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        RestorePlayerInput();
        RestorePlayerCamera();
    }

    private void IgnorePlayerEjectionCollisions(PlayerBrain player)
    {
        foreach (PlayerEjectionCollision existing in playerEjectionCollisions)
        {
            if (existing.player != player) continue;
            return;
        }

        List<Collider> potColliders = new();
        foreach (Collider potCollider in GetComponentsInChildren<Collider>())
        {
            if (!potCollider.enabled ||
                potCollider.GetComponentInParent<PotInteraction>() != this ||
                potCollider.GetComponentInParent<CatchableObj>() != null ||
                potCollider.GetComponentInParent<PlayerBrain>() != null) continue;
            potColliders.Add(potCollider);
        }
        PlayerEjectionCollision collision = new()
        {
            player = player,
            potColliders = potColliders.ToArray()
        };
        foreach (BodyPart part in player.BodyParts)
        {
            if (part == null || part.Col == null || part.Col.isTrigger) continue;
            foreach (Collider potCollider in collision.potColliders)
            {
                // Entry triggers stay active; suppress requests until the player clears them.
                if (potCollider.isTrigger) continue;
                if (Physics.GetIgnoreCollision(part.Col, potCollider)) continue;
                Physics.IgnoreCollision(part.Col, potCollider, true);
                collision.ignoredPairs.Add((part.Col, potCollider));
            }
        }
        playerEjectionCollisions.Add(collision);
    }

    private static bool IsPlayerClearOfPot(PlayerEjectionCollision collision)
    {
        Bounds potBounds = default;
        bool hasPotBounds = false;
        foreach (Collider potCollider in collision.potColliders)
        {
            if (potCollider == null || !potCollider.enabled || !potCollider.gameObject.activeInHierarchy) continue;
            if (hasPotBounds) potBounds.Encapsulate(potCollider.bounds);
            else potBounds = potCollider.bounds;
            hasPotBounds = true;
        }
        if (!hasPotBounds) return true;
        // Require the entire collider to leave the pot envelope, including its hollow interior.
        potBounds.Expand(EjectionClearance * 2f);
        if (!IsPlayerColliderClearOfPot(collision.player.Col, potBounds)) return false;
        foreach (BodyPart part in collision.player.BodyParts)
        {
            Collider bodyCollider = part != null ? part.Col : null;
            if (!IsPlayerColliderClearOfPot(bodyCollider, potBounds)) return false;
        }
        return true;
    }

    private static bool IsPlayerColliderClearOfPot(Collider bodyCollider, Bounds potBounds)
    {
        if (bodyCollider == null || !bodyCollider.enabled || !bodyCollider.gameObject.activeInHierarchy) return true;
        return !potBounds.Intersects(bodyCollider.bounds);
    }

    private static void RestorePlayerEjectionCollisions(PlayerEjectionCollision collision)
    {
        foreach (var pair in collision.ignoredPairs)
        {
            if (pair.body != null && pair.pot != null)
                Physics.IgnoreCollision(pair.body, pair.pot, false);
        }
        collision.ignoredPairs.Clear();
    }

    // 플레이어 부모 컴포넌트 탐색
    private static bool TryGetPlayer(Collider other, out PlayerBrain player)
    {
        player = other.GetComponentInParent<PlayerBrain>();
        return player != null;
    }

    #endregion

    #region State

    // 솥 상태 변경
    private void ChangeState(PotState nextState)
    {
        if (state == nextState) return;
        state = nextState;
        RefreshIngredientIcons();
    }

    #endregion

    #region Interface Implementations

    // IEntityParentReceiver: 서버 부모 변경으로 오브젝트 진입 
    public void HandleEntityAdded(CatchableObj entity)
    {
        ReceiveObjEntry(entity, state);
        TryStartCooking();
    }

    // IEntityParentReceiver: 서버 부모 변경으로 오브젝트 이탈 
    public void HandleEntityRemoved(CatchableObj entity)
    {
        pendingObjects.Remove(entity);
        RemoveStoredObject(entity);
    }

    // ICookReceiver: 서버 조리 시작 시간 
    public void HandleCookStart(CookStartPacket packet)
    {
        pendingCookingDuration = Mathf.Max(0f, packet.CookingTimeMs / 1000f);
        TryStartCooking();
    }

    // ICookReceiver: 일반 조리 일시정지 수신(미사용)
    public void HandleCookPause(CookPausePacket packet)
    {
    }

    // ICookReceiver: 일반 조리 완료 수신(미사용)
    public void HandleCookComplete(CookCompletePacket packet)
    {
    }

    // IPotNetworkReceiver: 서버 플레이어 진입 
    public void HandlePotPlayerEnter(ToolPlayerEnterPacket packet)
    {
        pendingPlayerEntries.Remove(packet.PlayerId);
        if (PlayerSpawnManager.Instance == null) return;
        if (!PlayerSpawnManager.Instance.TryGetPlayer(packet.PlayerId, out PlayerBrain player)) return;
        if (!trappedPlayers.Contains(player))
        {
            trappedPlayers.Add(player);
            if (state == PotState.Ejecting)
                playerEnteredDuringEjection = true;
            else
                PlayVisualOverride(IngredientIntakeHash, IngredientIntakeDuration, 0.04f);
        }
        if (player.Health != null)
            player.Health.IsDmgBlocked = true;
        PlayerGlow.SetActive(player, true);
        if (PlayerSpawnManager.Instance.IsMine(packet.PlayerId))
        {
            if (trappedLocalInput == null && player.Input != null)
            {
                trappedLocalInput = player.Input;
                previousInputBlocked = trappedLocalInput.IsInputBlocked;
                trappedLocalInput.IsInputBlocked = true;
            }
            ActivatePotCamera();
        }
    }

    public void ResetRespawnedPlayer(PlayerBrain player)
    {
        bool wasTrapped = trappedPlayers.Remove(player);
        pendingPlayerEntries.Remove(player.PlayerId);
        if (!wasTrapped) return;
        PlayerGlow.SetActive(player, false);
        if (PlayerSpawnManager.Instance != null && PlayerSpawnManager.Instance.IsMine(player.PlayerId))
        {
            RestorePlayerInput();
            RestorePlayerCamera();
        }
    }

    private void RestorePlayerInput()
    {
        if (trappedLocalInput != null)
            trappedLocalInput.IsInputBlocked = previousInputBlocked;
        trappedLocalInput = null;
    }

    private void ActivatePotCamera()
    {
        if (isPotCameraActive) return;
        if (potCamera == null)
            potCamera = GetComponentInChildren<CinemachineCamera>(true);
        if (potCamera == null) return;

        previousCameraPriority = potCamera.Priority;
        potCamera.Priority = potCameraPriority;
        isPotCameraActive = true;
    }

    private void RestorePlayerCamera()
    {
        if (!isPotCameraActive) return;
        if (potCamera != null)
            potCamera.Priority = previousCameraPriority;
        isPotCameraActive = false;
    }

    // IPotNetworkReceiver: 서버 배출 패킷 
    public void HandlePotEject(PotEjectPacket packet)
    {
        Ejector.Enqueue(packet);
    }

    // IPotNetworkReceiver: 서버 솥 조리 완료 
    public void HandlePotCookComplete(PotCookCompletePacket packet)
    {
        if (packet == null) return;

        pendingCookCompletePacket = packet;
        HideCookingUI();
        RemoveConsumedObj();
        PlayCookCompleteEffect(packet.DishEntityId);
    }

    private void PlayCookCompleteEffect(long completedDishId)
    {
        if (completedDishId <= 0 || completedDishId == lastCookCompleteEffectDishId) return;

        if (cookCompleteEffectPrefab == null)
            cookCompleteEffectPrefab = Resources.Load<DishCompleteVfx>("VFX/PotCookComplete");
        if (cookCompleteEffectPrefab == null)
        {
            Debug.LogWarning("솥 조리 완료 파티클 프리팹을 찾을 수 없습니다.", this);
            return;
        }

        Vector3 position;
        if (cookCompleteEffectOrigin != null)
            position = cookCompleteEffectOrigin.position;
        else
        {
            Renderer potRenderer = GetComponent<Renderer>();
            Bounds bounds = potRenderer != null ? potRenderer.bounds : new Bounds(transform.position, Vector3.zero);
            position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        DishCompleteVfx effect = Instantiate(cookCompleteEffectPrefab,
            position + cookCompleteEffectOffset, Quaternion.identity);
        effect.name = "솥_요리완성_파티클";
        float spread = Mathf.Max(0.1f, cookCompleteEffectSpread);
        effect.transform.localScale *= spread;
        foreach (ParticleSystem system in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            // 생성 범위만 Transform 배율을 적용하고, 개별 입자 크기는 유지합니다.
            var main = system.main;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startSpeed = ScaleEffectCurve(main.startSpeed, spread);
            main.gravityModifier = ScaleEffectCurve(main.gravityModifier, spread);
            var velocity = system.velocityOverLifetime;
            velocity.x = ScaleEffectCurve(velocity.x, spread);
            velocity.y = ScaleEffectCurve(velocity.y, spread);
            velocity.z = ScaleEffectCurve(velocity.z, spread);
            if (system.name == "확산고리")
                main.startSize = ScaleEffectCurve(main.startSize, spread);
        }
        effect.PlayOnce();
        lastCookCompleteEffectDishId = completedDishId;
    }

    private static ParticleSystem.MinMaxCurve ScaleEffectCurve(ParticleSystem.MinMaxCurve curve, float scale)
    {
        curve.constantMin *= scale;
        curve.constantMax *= scale;
        curve.curveMultiplier *= scale;
        return curve;
    }

    // IPunchReceiver: 솥 강제 배출 요청
    public void OnPunched(PlayerBrain player)
    {
        if (state == PotState.Ejecting) return;
        PlayVisualOverride(PunchRattleHash, PunchRattleDuration);
        if (storedObjects.Count == 0 && trappedPlayers.Count == 0) return;
        RequestForceEject();
    }

    #endregion

}

#region Editor
#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(PotInteraction))]
public class PotInteractionEditor : UnityEditor.Editor
{
    // 기본 Inspector와 디버그 배출 버튼 표시
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new UnityEditor.EditorGUI.DisabledScope(true))
            UnityEditor.EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        DrawPropertiesExcluding(serializedObject, "m_Script", "cookCompleteEffectSpread");
        UnityEditor.EditorGUILayout.PropertyField(serializedObject.FindProperty("cookCompleteEffectSpread"),
            new GUIContent("이펙트 확산 배율"));
        serializedObject.ApplyModifiedProperties();
        GUILayout.Space(8f);

        bool previousEnabled = GUI.enabled;
        GUI.enabled = Application.isPlaying;

        if (GUILayout.Button("Debug Force Eject"))
            ((PotInteraction)target).DebugForceEject();

        GUI.enabled = previousEnabled;
    }
}
#endif
#endregion
