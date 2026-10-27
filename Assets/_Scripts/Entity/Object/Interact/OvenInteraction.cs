using Cysharp.Threading.Tasks;
using DG.Tweening;
using Protocol;
using Server;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(100)]
public class OvenInteraction : MapObjInteraction, IEntityParentReceiver, ICookReceiver, IClientStageResettable
{
    #region Inspector

    [Header("컴포넌트 연결")]
    [FormerlySerializedAs("mainButton")]
    [SerializeField] private Transform mainBtn;
    [FormerlySerializedAs("buttonRelay")]
    [SerializeField] private ColliderRelay btnRelay;
    [SerializeField] private BoxCollider innerTrigger;
    [SerializeField] private OvenDoorController doorController;
    [SerializeField] private AudioSource completeAudio;

    [Header("버튼 입력 및 연출")]
    [FormerlySerializedAs("minimumCollisionSpeed")]
    [SerializeField, Min(0f)] private float minCollisionSpeed = 1f;
    [FormerlySerializedAs("pressedButtonZ")]
    [SerializeField] private float pressedBtnZ = 0.576f;
    [FormerlySerializedAs("buttonPressDuration")]
    [SerializeField, Min(0.01f)] private float btnPressDuration = 0.1f;
    [FormerlySerializedAs("buttonReturnDuration")]
    [SerializeField, Min(0.01f)] private float btnReturnDuration = 0.1f;

    [Header("조리 및 문 열림")]
    [SerializeField, Min(0.1f)] private float startResponseTimeout = 2f;
    // 기존 프리팹과 씬의 저장값을 유지하기 위해 필드명은 보존한다. 모든 조리에 적용된다.
    [SerializeField, Min(0f), InspectorName("최소 닫힘 유지 시간")]
    private float emptyOvenWaitDuration = 3f;

    [Header("내부 플레이어 피해")]
    [FormerlySerializedAs("playerDamagePerTick")]
    [SerializeField, Min(0f)] private float playerDmgPerTick = 10f;

    #endregion

    #region Runtime State

    private const float PlayerDmgInterval = 1f;
    private enum OvenState { Idle, Waiting, Cooking, EmptyPerformance, Opening }

    // Waiting은 서버 응답 대기, EmptyPerformance는 서버 조리 없이 진행하는 로컬 동작이다.
    private OvenState state;
    private float responseDeadline;
    private float cookingDeadline;
    // 무한대이면 아직 문이 완전히 닫히지 않아 유지 시간 계산 전이다.
    private float minOpenTime = float.PositiveInfinity;

    private Vector3 btnRestPos;
    private Tween btnTween;

    // 서버 등록 재료 / 버튼을 누를 때 감지한 전송 대상 / 물리 고정 전 원본 상태.
    private readonly Dictionary<long, IngredientReaction> currentIngredients = new();
    private readonly Dictionary<long, CatchableObj> capturedIngredients = new();
    private readonly Dictionary<long, FrozenIngredient> frozenIngredients = new();
    private readonly List<long> removedIngredients = new(); // 순회 중 삭제를 피하기 위한 임시 목록.
    private readonly HashSet<PlayerBrain> capturedPlayers = new(); // 서버 입장 요청에만 사용한다.

    // 피해와 회복 보류는 각 클라의 자기 플레이어에게만 적용한다.
    private float nextPlayerDmgTime;
    private LocalPlayerStateResolver recoveryPausedResolver;

    private int requestVersion; // 이전 비동기 요청이 새 동작에 개입하지 못하게 하는 번호.
    private Coroutine subscribeRoutine;
    private ServerManager subscribedServer;

    public bool IsLocked => state != OvenState.Idle;

    #endregion

    #region Unity Callbacks

    private void Awake()
    {
        if (mainBtn != null) btnRestPos = mainBtn.localPosition;
    }

    private void OnEnable()
    {
        if (btnRelay != null) btnRelay.CollisionEntered += HandleButtonCollision;
        PlayerActionController.RemotePunchPlayed += HandleRemotePunch;
        subscribeRoutine = StartCoroutine(SubscribeWhenReady());
    }

    private void Update()
    {
        RemoveUnavailableIngredients();
        UpdatePlayerDamage();
        UpdateMinimumClosedTime();
        if (state == OvenState.Waiting && Time.unscaledTime >= responseDeadline)
        {
            // 서버 응답이 없으면 최소 닫힘 시간을 지키는 로컬 동작으로 마무리한다.
            state = OvenState.EmptyPerformance;
            nextPlayerDmgTime = Time.time + PlayerDmgInterval;
        }
        else if (state == OvenState.Cooking && Time.time >= cookingDeadline && CanOpenDoor())
            FinishCooking();
        else if (state == OvenState.EmptyPerformance && CanOpenDoor())
            FinishCooking();
        else if (state == OvenState.Opening
            && (doorController == null || !doorController.IsMoving) && !IsButtonMoving())
        {
            ReleasePlayerRecovery();
            state = OvenState.Idle;
            capturedIngredients.Clear();
            capturedPlayers.Clear();
        }
    }

    private void FixedUpdate()
    {
        HoldIngredients(true);
    }

    private void LateUpdate()
    {
        HoldIngredients(false);
    }

    private void OnDisable()
    {
        if (btnRelay != null) btnRelay.CollisionEntered -= HandleButtonCollision;
        PlayerActionController.RemotePunchPlayed -= HandleRemotePunch;
        if (subscribeRoutine != null) StopCoroutine(subscribeRoutine);
        subscribeRoutine = null;
        if (subscribedServer != null)
            subscribedServer.Router.OnEntityDestroyed -= HandleEntitiesDestroyed;
        subscribedServer = null;
        requestVersion++;
        btnTween?.Kill();
        btnTween = null;
        if (mainBtn != null) mainBtn.localPosition = btnRestPos;
        HideAllGauges();
        RestoreIngredients();
        ReleasePlayerRecovery();
        currentIngredients.Clear();
        capturedIngredients.Clear();
        capturedPlayers.Clear();
        state = OvenState.Idle;
    }

    #endregion

    #region Public API

    public void ResetClientStage()
    {
        requestVersion++;
        btnTween?.Kill();
        btnTween = null;
        if (mainBtn != null) mainBtn.localPosition = btnRestPos;
        HideAllGauges();
        RestoreIngredients();
        ReleasePlayerRecovery();
        currentIngredients.Clear();
        capturedIngredients.Clear();
        capturedPlayers.Clear();
        removedIngredients.Clear();
        responseDeadline = 0f;
        cookingDeadline = 0f;
        minOpenTime = float.PositiveInfinity;
        nextPlayerDmgTime = 0f;
        state = OvenState.Idle;
        if (doorController != null) doorController.OpenDoor();
    }
    // 버튼 입력 진입점. 로컬 입력일 때만 서버 요청을 보낸다.
    public void PressButton(PlayerBrain player)
    {
        if (player == null || PlayerSpawnManager.Instance == null) return;
        BeginButtonPress(PlayerSpawnManager.Instance.IsMine(player.PlayerId));
    }

    // 서버가 확정한 재료 등록과 조리 상태를 반영한다.
    public void HandleEntityAdded(CatchableObj entity)
    {
        if (entity == null || !entity.TryGetComponent(out IngredientReaction reaction)) return;
        currentIngredients[entity.NetworkId] = reaction;
        if (state == OvenState.Idle) BeginPendingLock();
        if (state == OvenState.Waiting || state == OvenState.Cooking)
            FreezeIngredient(entity);
        if (state == OvenState.Cooking)
            reaction.GaugeUI?.StartFill(Mathf.Max(0f, cookingDeadline - Time.time));
    }

    public void HandleEntityRemoved(CatchableObj entity)
    {
        if (entity != null) RemoveIngredient(entity.NetworkId, true);
    }

    // 서버 조리 시간과 로컬 최소 닫힘 시간은 별도로 관리한다.
    public void HandleCookStart(CookStartPacket packet)
    {
        if (doorController == null) return;
        bool alreadyPressed = state == OvenState.Waiting || state == OvenState.EmptyPerformance;
        requestVersion++;
        if (state != OvenState.Cooking)
            nextPlayerDmgTime = Time.time + PlayerDmgInterval;
        state = OvenState.Cooking;
        cookingDeadline = Time.time + Mathf.Max(0f, packet.CookingTimeMs / 1000f);
        if (!alreadyPressed) PlayButtonMotion();
        EnsureDoorClosed();
        foreach (IngredientReaction reaction in currentIngredients.Values)
        {
            if (reaction == null) continue;
            FreezeIngredient(reaction.Catchable);
            reaction.GaugeUI?.StartFill(Mathf.Max(0f, cookingDeadline - Time.time));
        }
    }

    public void HandleCookPause(CookPausePacket packet)
    {
        // 오븐은 중단 불가. Pause로 문이나 잠금을 해제하지 않는다.
    }

    public void HandleCookComplete(CookCompletePacket packet)
    {
        if ((packet.CookType & IngredientState.Roasted) == 0) return;
        if (!currentIngredients.TryGetValue(packet.IngredientEntityId, out IngredientReaction reaction)
            || reaction == null) return;
        reaction.GaugeUI?.Hide();
        reaction.ApplyServerAction((IngredientAction)packet.CookType);
    }

    #endregion

    #region Button Input and Animation

    private void HandleButtonCollision(Collision collision)
    {
        if (collision.collider.transform.IsChildOf(transform)) return;
        if (collision.collider.GetComponentInParent<PlayerBrain>() != null) return;
        if (collision.relativeVelocity.sqrMagnitude < minCollisionSpeed * minCollisionSpeed) return;
        BeginButtonPress(Router != null && Router.IsRegistrar);
    }

    private void HandleRemotePunch(PlayerBrain player)
    {
        if (IsLocked || mainBtn == null || player == null) return;
        Transform facing = player.FacingRoot != null ? player.FacingRoot : player.transform;
        Vector3 offset = mainBtn.position - facing.position;
        if (offset.sqrMagnitude > player.InteractDistance * player.InteractDistance) return;
        if (Vector3.Dot(facing.forward, offset.normalized) < 0.5f) return;
        // 문 연출은 각 클라에서 실행하고 요청은 행동한 클라만 전송한다.
        BeginButtonPress(false);
    }

    private void BeginButtonPress(bool sendsRequest)
    {
        if (IsLocked || IsButtonMoving()) return;
        if (mainBtn == null || innerTrigger == null || doorController == null)
        {
            Debug.LogError("Assign MainBtn, InnerTrigger and OvenDoorController.", this);
            return;
        }
        bool hasRoastableIngredients = CaptureContents();
        BeginPendingLock();
        // 재료가 없으면 서버 조리 시작 응답 없이 로컬에서 진행한다.
        if (!hasRoastableIngredients)
        {
            nextPlayerDmgTime = Time.time + PlayerDmgInterval;
            state = OvenState.EmptyPerformance;
            return;
        }
        if (!sendsRequest || !IsRegistered || ServerManager.Instance == null) return;
        SendStartRequests(requestVersion).Forget();
    }

    private void PlayButtonMotion()
    {
        if (mainBtn == null || IsButtonMoving()) return;
        btnTween = DOTween.Sequence()
            .Append(mainBtn.DOLocalMoveZ(pressedBtnZ, btnPressDuration).SetEase(Ease.OutSine))
            .Append(mainBtn.DOLocalMoveZ(btnRestPos.z, btnReturnDuration).SetEase(Ease.InSine))
            .OnComplete(() => btnTween = null);
    }

    private bool IsButtonMoving() => btnTween != null && btnTween.IsActive();

    #endregion

    #region Cooking and Door Cycle

    private void BeginPendingLock()
    {
        state = OvenState.Waiting;
        responseDeadline = Time.unscaledTime + startResponseTimeout;
        requestVersion++;
        PlayButtonMotion();
        EnsureDoorClosed();
    }

    private void EnsureDoorClosed()
    {
        if (doorController == null || doorController.IsClosed) return;
        minOpenTime = float.PositiveInfinity;
        doorController.CloseDoor();
    }

    private void UpdateMinimumClosedTime()
    {
        if (state != OvenState.Waiting && state != OvenState.Cooking && state != OvenState.EmptyPerformance)
            return;
        if (!float.IsPositiveInfinity(minOpenTime) || doorController == null || !doorController.IsClosed || doorController.IsMoving)
            return;

        // 완전히 닫힌 시점부터 최소 유지 시간을 계산한다.
        minOpenTime = Time.time + emptyOvenWaitDuration;
    }

    private bool CanOpenDoor()
    {
        return Time.time >= minOpenTime &&
            doorController != null && doorController.IsClosed && !doorController.IsMoving;
    }

    // 자동 개방 직전 내부 생존자를 즉사시키고, 문이 열릴 때까지 회복을 보류한다.
    private void FinishCooking()
    {
        if (TryGetLocalPlayerInside(out PlayerBrain player))
        {
            PausePlayerRecovery(player);
            player.Health.InstantDeath();
        }
        if (completeAudio != null) completeAudio.Play();
        BeginOpening();
    }

    private void BeginOpening()
    {
        requestVersion++;
        state = OvenState.Opening;
        HideAllGauges();
        RestoreIngredients();
        if (doorController != null) doorController.OpenDoor();
    }

    #endregion

    #region Network

    private IEnumerator SubscribeWhenReady()
    {
        yield return new WaitUntil(() => ServerManager.Instance != null);
        subscribedServer = ServerManager.Instance;
        subscribedServer.Router.OnEntityDestroyed += HandleEntitiesDestroyed;
        subscribeRoutine = null;
    }

    private async UniTask SendStartRequests(int version)
    {
        try
        {
            foreach (CatchableObj entity in new List<CatchableObj>(capturedIngredients.Values))
            {
                if (!isActiveAndEnabled || version != requestVersion) return;
                if (entity == null || entity.NetworkId == 0) continue;
                EntityInsertPacket insert = new() { SubjectEntityId = entity.NetworkId, TargetEntityId = NetworkId };
                await ServerManager.Instance.SendData(PacketSerializer.Serialize(insert));
            }
            // TODO: 서버는 발신자만 등록한다. 다른 내부 플레이어 지정 등록은 서버 반영 후 연결.
            foreach (PlayerBrain player in new List<PlayerBrain>(capturedPlayers))
            {
                if (!isActiveAndEnabled || version != requestVersion) return;
                if (player == null || !PlayerSpawnManager.Instance.IsMine(player.PlayerId)) continue;
                ToolPlayerEnterPacket enter = new() { ToolEntityId = NetworkId, PlayerId = player.PlayerId };
                await ServerManager.Instance.SendData(PacketSerializer.Serialize(enter));
            }
            if (!isActiveAndEnabled || version != requestVersion) return;
            EntityInteractPacket start = new() { TargetEntityId = NetworkId };
            await ServerManager.Instance.SendData(PacketSerializer.Serialize(start));
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
            if (isActiveAndEnabled && version == requestVersion)
            {
                state = OvenState.EmptyPerformance;
                nextPlayerDmgTime = Time.time + PlayerDmgInterval;
            }
        }
    }

    private void HandleEntitiesDestroyed(IReadOnlyList<EntityDestroyPacket> packets)
    {
        foreach (EntityDestroyPacket packet in packets) RemoveIngredient(packet.EntityId, false);
    }

    #endregion

    #region Ingredient Storage and Physics

    // 버튼을 누른 순간 내부 대상을 수집하고 조리 가능한 재료 유무를 반환한다.
    private bool CaptureContents()
    {
        bool hasRoastableIngredients = false;
        capturedIngredients.Clear();
        capturedPlayers.Clear();
        Physics.SyncTransforms();
        Vector3 scale = innerTrigger.transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Collider[] contents = Physics.OverlapBox(innerTrigger.transform.TransformPoint(innerTrigger.center),
            Vector3.Scale(innerTrigger.size * 0.5f, scale), innerTrigger.transform.rotation,
            ~0, QueryTriggerInteraction.Collide);
        foreach (Collider other in contents)
        {
            if (other.transform.IsChildOf(transform)) continue;
            PlayerBrain player = other.GetComponentInParent<PlayerBrain>();
            if (player != null)
            {
                capturedPlayers.Add(player);
                continue;
            }
            CatchableObj entity = other.GetComponentInParent<CatchableObj>();
            if (entity == null || entity.Category != EntityCategory.Ingredient || entity.NetworkId == 0) continue;
            if (entity.transform.parent != null
                && entity.transform.parent.GetComponentInParent<CatchableObj>() != null) continue;
            capturedIngredients[entity.NetworkId] = entity;
            if (CanRoast(entity)) hasRoastableIngredients = true;
        }

        return hasRoastableIngredients;
    }

    private static bool CanRoast(CatchableObj entity)
    {
        if (entity.Data is Ingredient data && (data.conditionFlag & (int)IngredientAction.Cook) != 0)
            return false;
        return !entity.TryGetComponent(out IngredientReaction reaction)
            || !reaction.IsActionCompleted(IngredientAction.Cook);
    }

    private void FreezeIngredient(CatchableObj entity)
    {
        if (entity == null || frozenIngredients.ContainsKey(entity.NetworkId)) return;
        bool scriptedMotion = entity.TryGetComponent(out IngredientTraitBounce bounce) && bounce.IsSimulating;
        frozenIngredients[entity.NetworkId] = new FrozenIngredient
        {
            Entity = entity, CanBePicked = entity.CanBePicked,
            ColliderEnabled = entity.Col != null && entity.Col.enabled,
            IsKinematic = entity.Rb != null && entity.Rb.isKinematic && !scriptedMotion,
            UseGravity = entity.Rb != null && (entity.Rb.useGravity || scriptedMotion),
            Pos = entity.transform.position,
            Rot = entity.transform.rotation
        };
        entity.ChangePickState(false);
        entity.SetPhysicsState(false);
    }

    private void RestoreIngredient(long id)
    {
        if (!frozenIngredients.Remove(id, out FrozenIngredient frozen)) return;
        CatchableObj entity = frozen.Entity;
        if (entity == null || entity.NetworkId != id) return;
        entity.ChangePickState(frozen.CanBePicked);
        if (entity.Col != null) entity.Col.enabled = frozen.ColliderEnabled;
        if (entity.Rb != null)
        {
            if (frozen.IsKinematic) entity.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            entity.Rb.isKinematic = frozen.IsKinematic;
            if (!frozen.IsKinematic) entity.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            entity.Rb.useGravity = frozen.UseGravity;
        }
    }

    private void RestoreIngredients()
    {
        foreach (long id in new List<long>(frozenIngredients.Keys)) RestoreIngredient(id);
    }

    private void HoldIngredients(bool physicsTick)
    {
        foreach (var item in frozenIngredients)
        {
            FrozenIngredient frozen = item.Value;
            CatchableObj entity = frozen.Entity;
            if (entity == null || entity.NetworkId != item.Key || !entity.gameObject.activeInHierarchy) continue;
            entity.transform.SetPositionAndRotation(frozen.Pos, frozen.Rot);
            if (!physicsTick || entity.Rb == null) continue;
            // 자체 이동 트레잇의 MovePosition도 조리 중에는 고정 위치로 덮어쓴다.
            entity.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            entity.Rb.isKinematic = true;
            entity.Rb.MovePosition(frozen.Pos);
            entity.Rb.MoveRotation(frozen.Rot);
        }
    }

    private void RemoveUnavailableIngredients()
    {
        removedIngredients.Clear();
        foreach (var item in currentIngredients)
            if (item.Value == null || item.Value.Catchable == null
                || !item.Value.gameObject.activeInHierarchy || item.Value.Catchable.NetworkId != item.Key)
                removedIngredients.Add(item.Key);
        foreach (long id in removedIngredients) RemoveIngredient(id, false);
    }

    private void RemoveIngredient(long id, bool restore)
    {
        capturedIngredients.Remove(id);
        if (currentIngredients.Remove(id, out IngredientReaction reaction) && reaction != null)
            reaction.GaugeUI?.Hide();
        if (restore) RestoreIngredient(id);
        else frozenIngredients.Remove(id);
    }

    private void HideAllGauges()
    {
        foreach (IngredientReaction reaction in currentIngredients.Values)
            if (reaction != null) reaction.GaugeUI?.Hide();
    }

    // 조리 중 재료를 고정했다가 기존 물리 및 집기 상태로 복원할 때 사용한다.
    private sealed class FrozenIngredient
    {
        public CatchableObj Entity;
        public bool CanBePicked;
        public bool ColliderEnabled;
        public bool IsKinematic;
        public bool UseGravity;
        public Vector3 Pos;
        public Quaternion Rot;
    }

    #endregion

    #region Player Damage and Recovery

    // 가열 중 자기 플레이어에게만 틱 피해와 회복 보류를 적용한다.
    private void UpdatePlayerDamage()
    {
        bool isHeating = state == OvenState.Cooking || state == OvenState.EmptyPerformance;
        if (!isHeating) return;

        bool inside = TryGetLocalPlayerInside(out PlayerBrain player);
        if (inside)
            PausePlayerRecovery(player);
        else
            ReleasePlayerRecovery();

        if (Time.time < nextPlayerDmgTime) return;
        nextPlayerDmgTime = Time.time + PlayerDmgInterval;
        if (inside) player.Health.TakeDamage(playerDmgPerTick);
    }

    private bool TryGetLocalPlayerInside(out PlayerBrain player)
    {
        player = null;
        PlayerSpawnManager manager = PlayerSpawnManager.Instance;
        if (innerTrigger == null || manager == null || string.IsNullOrEmpty(manager.MyID) ||
            !manager.TryGetPlayer(manager.MyID, out player) || player == null ||
            player.Health == null || player.Col == null || !player.gameObject.activeInHierarchy)
            return false;

        // 래그돌 중 트리거로 바뀐 루트 콜라이더도 내부 판정에 포함한다.
        return Physics.ComputePenetration(
            innerTrigger, innerTrigger.transform.position, innerTrigger.transform.rotation,
            player.Col, player.Col.transform.position, player.Col.transform.rotation,
            out _, out _);
    }

    private void PausePlayerRecovery(PlayerBrain player)
    {
        if (player.StateResolver is not LocalPlayerStateResolver resolver) return;
        if (recoveryPausedResolver == resolver) return;
        ReleasePlayerRecovery();
        recoveryPausedResolver = resolver;
        resolver.SetRagdollRecoveryPaused(true);
    }

    private void ReleasePlayerRecovery()
    {
        recoveryPausedResolver?.SetRagdollRecoveryPaused(false);
        recoveryPausedResolver = null;
    }

    #endregion

}
