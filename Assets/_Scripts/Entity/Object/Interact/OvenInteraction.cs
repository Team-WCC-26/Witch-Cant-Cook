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

    [Header("가동 조명")]
    [SerializeField] private Light[] operatingLights = System.Array.Empty<Light>();

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
    [SerializeField, Min(0f), InspectorName("문이 닫힌 후 가동 지연")]
    private float startDelayAfterClose = 1f;
    // 기존 프리팹과 씬의 저장값을 유지하기 위해 필드명은 보존한다. 모든 조리에 적용된다.
    [SerializeField, Min(0f), InspectorName("최소 닫힘 유지 시간")]
    private float emptyOvenWaitDuration = 3f;

    #endregion

    #region Runtime State

    private enum OvenState { Idle, Waiting, Cooking, Opening }

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
    private ColliderRelay innerRelay;
    private PlayerBrain localPlayerInside;
    private string localPlayerId;
    private long enteredToolId;
    private bool localPlayerEntrySent;
    private UniTaskCompletionSource playerEntryConfirmation;
    private const float PlayerEntryResponseTimeout = 2f;
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
        RefreshOperatingLights();
    }

    private void OnEnable()
    {
        RefreshOperatingLights();
        if (btnRelay != null) btnRelay.CollisionEntered += HandleButtonCollision;
        innerRelay = innerTrigger != null ? innerTrigger.GetComponent<ColliderRelay>() : null;
        if (innerRelay != null)
        {
            innerRelay.TriggerEntered += HandlePlayerEnter;
            innerRelay.TriggerStayed += HandlePlayerEnter;
            innerRelay.TriggerExited += HandlePlayerExit;
        }
        subscribeRoutine = StartCoroutine(SubscribeWhenReady());
    }

    private void Update()
    {
        RemoveUnavailableIngredients();
        UpdatePlayerRecovery();
        UpdateMinimumClosedTime();
        if (state == OvenState.Waiting && Time.unscaledTime >= responseDeadline)
        {
            CancelPendingStart();
        }
        else if (state == OvenState.Cooking && Time.time >= cookingDeadline && CanOpenDoor())
            FinishCooking();
        else if (state == OvenState.Opening
            && (doorController == null || !doorController.IsMoving) && !IsButtonMoving())
        {
            ReleasePlayerRecovery();
            SetState(OvenState.Idle);
            capturedIngredients.Clear();
        }
    }

    private void FixedUpdate()
    {
        UpdateLocalPlayerPresence();
        HoldIngredients(true);
    }

    private void LateUpdate()
    {
        HoldIngredients(false);
    }

    private void OnDisable()
    {
        if (btnRelay != null) btnRelay.CollisionEntered -= HandleButtonCollision;
        if (innerRelay != null)
        {
            innerRelay.TriggerEntered -= HandlePlayerEnter;
            innerRelay.TriggerStayed -= HandlePlayerEnter;
            innerRelay.TriggerExited -= HandlePlayerExit;
        }
        ClearLocalPlayer(true);
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
        SetState(OvenState.Idle);
    }

    #endregion

    #region Public API

    public void HandlePlayerEntryConfirmed(ToolPlayerEnterPacket packet)
    {
        if (packet.ToolEntityId != enteredToolId || packet.PlayerId != localPlayerId) return;
        playerEntryConfirmation?.TrySetResult();
    }

    public void ResetClientStage()
    {
        ClearLocalPlayer(false);
        requestVersion++;
        btnTween?.Kill();
        btnTween = null;
        if (mainBtn != null) mainBtn.localPosition = btnRestPos;
        HideAllGauges();
        RestoreIngredients();
        ReleasePlayerRecovery();
        currentIngredients.Clear();
        capturedIngredients.Clear();
        removedIngredients.Clear();
        responseDeadline = 0f;
        cookingDeadline = 0f;
        minOpenTime = float.PositiveInfinity;
        SetState(OvenState.Idle);
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
        if (state == OvenState.Cooking)
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
        bool alreadyCooking = state == OvenState.Cooking;
        requestVersion++;
        SetState(OvenState.Cooking);
        cookingDeadline = Time.time + Mathf.Max(0f, packet.CookingTimeMs / 1000f);
        if (!alreadyCooking) PlayButtonMotion();
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

    private void BeginButtonPress(bool sendsRequest)
    {
        if (!sendsRequest || IsLocked || IsButtonMoving()) return;
        if (mainBtn == null || innerTrigger == null || doorController == null)
        {
            Debug.LogError("Assign MainBtn, InnerTrigger and OvenDoorController.", this);
            return;
        }
        if (!IsRegistered || ServerManager.Instance == null) return;
        CaptureContents();
        BeginPendingLock();
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
        SetState(OvenState.Waiting);
        responseDeadline = Time.unscaledTime + startResponseTimeout;
        requestVersion++;
    }

    private void CancelPendingStart()
    {
        requestVersion++;
        HideAllGauges();
        RestoreIngredients();
        ReleasePlayerRecovery();
        capturedIngredients.Clear();
        SetState(OvenState.Idle);
    }

    private void EnsureDoorClosed()
    {
        if (doorController == null || doorController.IsClosed) return;
        minOpenTime = float.PositiveInfinity;
        doorController.CloseDoor();
    }

    private void UpdateMinimumClosedTime()
    {
        if (state != OvenState.Cooking) return;
        if (!float.IsPositiveInfinity(minOpenTime) || doorController == null || !doorController.IsClosed || doorController.IsMoving)
            return;

        // 완전히 닫힌 시점부터 최소 유지 시간을 계산한다.
        minOpenTime = Time.time + startDelayAfterClose + emptyOvenWaitDuration;
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
        SetState(OvenState.Opening);
        HideAllGauges();
        RestoreIngredients();
        if (doorController != null) doorController.OpenDoor();
    }

    #endregion

    #region Operating Lights

    private void SetState(OvenState nextState)
    {
        state = nextState;
        RefreshOperatingLights();
    }

    private void RefreshOperatingLights()
    {
        bool isOperating = isActiveAndEnabled && state == OvenState.Cooking;
        if (operatingLights == null) return;
        foreach (Light operatingLight in operatingLights)
            if (operatingLight != null) operatingLight.enabled = isOperating;
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
            if (!isActiveAndEnabled || version != requestVersion) return;
            EntityInteractPacket start = new() { TargetEntityId = NetworkId };
            await ServerManager.Instance.SendData(PacketSerializer.Serialize(start));
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
            if (isActiveAndEnabled && version == requestVersion)
            {
                CancelPendingStart();
            }
        }
    }

    private void HandleEntitiesDestroyed(IReadOnlyList<EntityDestroyPacket> packets)
    {
        foreach (EntityDestroyPacket packet in packets) RemoveIngredient(packet.EntityId, false);
    }

    #endregion

    #region Ingredient Storage and Physics

    private void CaptureContents()
    {
        capturedIngredients.Clear();
        Physics.SyncTransforms();
        Vector3 scale = innerTrigger.transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Collider[] contents = Physics.OverlapBox(innerTrigger.transform.TransformPoint(innerTrigger.center),
            Vector3.Scale(innerTrigger.size * 0.5f, scale), innerTrigger.transform.rotation,
            ~0, QueryTriggerInteraction.Collide);
        foreach (Collider other in contents)
        {
            if (other.transform.IsChildOf(transform)) continue;
            if (other.GetComponentInParent<PlayerBrain>() != null) continue;
            CatchableObj entity = other.GetComponentInParent<CatchableObj>();
            if (entity == null || entity.Category != EntityCategory.Ingredient || entity.NetworkId == 0) continue;
            if (entity.transform.parent != null
                && entity.transform.parent.GetComponentInParent<CatchableObj>() != null) continue;
            capturedIngredients[entity.NetworkId] = entity;
        }
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

    #region Player Recovery

    private bool TryGetLocalPlayerBody(Collider other, out PlayerBrain player)
    {
        player = other != null ? other.GetComponent<PlayerBrain>() : null;
        return player != null && other == player.Col && PlayerSpawnManager.Instance != null &&
            PlayerSpawnManager.Instance.IsMine(player.PlayerId);
    }

    private void HandlePlayerEnter(Collider other)
    {
        if (!TryGetLocalPlayerBody(other, out PlayerBrain player)) return;
        if (localPlayerInside != null) return;
        localPlayerInside = player;
        localPlayerId = player.PlayerId;
        TrySendPlayerEntry();
    }

    private void HandlePlayerExit(Collider other)
    {
        if (!TryGetLocalPlayerBody(other, out PlayerBrain player) || localPlayerInside != player) return;
        if (TryGetLocalPlayerInside(out _)) return;
        ClearLocalPlayer(true);
    }

    private void UpdateLocalPlayerPresence()
    {
        if (string.IsNullOrEmpty(localPlayerId)) return;
        if (localPlayerInside == null || !TryGetLocalPlayerInside(out _))
        {
            ClearLocalPlayer(true);
            return;
        }
        TrySendPlayerEntry();
    }

    private void TrySendPlayerEntry()
    {
        if (localPlayerEntrySent || localPlayerInside == null || NetworkId == 0 ||
            ServerManager.Instance == null || StageReturnFlow.IsInputBlocked) return;
        localPlayerEntrySent = true;
        enteredToolId = NetworkId;
        ToolPlayerEnterPacket packet = new() { ToolEntityId = enteredToolId, PlayerId = localPlayerId };
        playerEntryConfirmation = new UniTaskCompletionSource();
        SendPlayerEntryWithRetry(packet, playerEntryConfirmation).Forget();
    }

    private void ClearLocalPlayer(bool sendsExit)
    {
        playerEntryConfirmation?.TrySetResult();
        playerEntryConfirmation = null;
        bool send = sendsExit && localPlayerEntrySent && ServerManager.Instance != null;
        long toolId = enteredToolId;
        string playerId = localPlayerId;
        localPlayerInside = null;
        localPlayerId = null;
        enteredToolId = 0;
        localPlayerEntrySent = false;
        if (!send) return;
        ToolPlayerExitPacket packet = new() { ToolEntityId = toolId, PlayerId = playerId };
        SendPlayerPresence(PacketSerializer.Serialize(packet)).Forget();
    }

    private async UniTask SendPlayerEntryWithRetry(ToolPlayerEnterPacket packet, UniTaskCompletionSource confirmation)
    {
        var cancellationToken = this.GetCancellationTokenOnDestroy();
        byte[] data = PacketSerializer.Serialize(packet);
        string failure = "No server entry confirmation";
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (confirmation.Task.Status != UniTaskStatus.Pending) return;
                try
                {
                    if (ServerManager.Instance == null)
                        throw new System.InvalidOperationException("ServerManager is unavailable");
                    await ServerManager.Instance.SendData(data);
                    failure = "No server entry confirmation";
                    float deadline = Time.realtimeSinceStartup + PlayerEntryResponseTimeout;
                    while (confirmation.Task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                catch (System.OperationCanceledException)
                {
                    return;
                }
                catch (System.Exception exception)
                {
                    failure = exception.Message;
                }
                if (confirmation.Task.Status != UniTaskStatus.Pending) return;
            }
            Debug.LogError($"[Oven] Player entry could not be confirmed after two attempts. ToolId={packet.ToolEntityId}, PlayerId={packet.PlayerId}, Reason={failure}", this);
        }
        finally
        {
            if (playerEntryConfirmation == confirmation) playerEntryConfirmation = null;
        }
    }
    private async UniTask SendPlayerPresence(byte[] packet)
    {
        try
        {
            await ServerManager.Instance.SendData(packet);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }
    }
    private void UpdatePlayerRecovery()
    {
        bool isHeating = state == OvenState.Cooking;
        if (!isHeating) return;

        bool inside = TryGetLocalPlayerInside(out PlayerBrain player);
        if (inside)
            PausePlayerRecovery(player);
        else
            ReleasePlayerRecovery();
    }

    private bool TryGetLocalPlayerInside(out PlayerBrain player)
    {
        player = null;
        PlayerSpawnManager manager = PlayerSpawnManager.Instance;
        if (innerTrigger == null || manager == null || string.IsNullOrEmpty(manager.MyID) ||
            !manager.TryGetPlayer(manager.MyID, out player) || player == null ||
            player.Health == null || player.Col == null || !player.gameObject.activeInHierarchy)
            return false;

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
