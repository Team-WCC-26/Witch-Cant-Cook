using Cysharp.Threading.Tasks;
using DG.Tweening;
using Protocol;
using Server;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class OvenInteraction : MapObjInteraction, IEntityParentReceiver, ICookReceiver, IClientStageResettable
{
    public void ResetClientStage()
    {
        requestVersion++;
        buttonTween?.Kill();
        buttonTween = null;
        if (mainButton != null) mainButton.localPosition = buttonRestPosition;
        HideAllGauges();
        RestoreIngredients();
        currentIngredients.Clear();
        pendingIngredients.Clear();
        capturedIngredients.Clear();
        capturedPlayers.Clear();
        state = OvenState.Idle;
    }
    [SerializeField] private Transform mainButton;
    [SerializeField] private ColliderRelay buttonRelay;
    [SerializeField] private BoxCollider innerTrigger;
    [SerializeField] private OvenDoorController doorController;
    [SerializeField, Min(0f)] private float minimumCollisionSpeed = 1f;
    [SerializeField] private float pressedButtonZ = 0.576f;
    [SerializeField, Min(0.01f)] private float buttonPressDuration = 0.1f;
    [SerializeField, Min(0.01f)] private float buttonReturnDuration = 0.1f;
    [SerializeField, Min(0.1f)] private float startResponseTimeout = 2f;
    [SerializeField, Min(0f)] private float emptyOvenWaitDuration = 3f;
    [SerializeField] private AudioSource completeAudio;

    private enum OvenState { Idle, Waiting, Cooking, EmptyPerformance, Opening }
    private readonly Dictionary<long, IngredientReaction> currentIngredients = new();
    private readonly HashSet<long> pendingIngredients = new();
    private readonly Dictionary<long, CatchableObj> capturedIngredients = new();
    private readonly HashSet<PlayerBrain> capturedPlayers = new();
    private readonly Dictionary<long, FrozenIngredient> frozenIngredients = new();
    private readonly List<long> removedIngredients = new();
    private OvenState state;
    private Vector3 buttonRestPosition;
    private Tween buttonTween;
    private float responseDeadline;
    private float cookingDeadline;
    private bool emptyWaitStarted;
    private int requestVersion;
    private Coroutine subscribeRoutine;
    private ServerManager subscribedServer;

    public bool IsLocked => state != OvenState.Idle;

    private sealed class FrozenIngredient
    {
        public CatchableObj Entity;
        public bool CanBePicked;
        public bool ColliderEnabled;
        public bool IsKinematic;
        public bool UseGravity;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    #region Unity Callbacks
    private void Awake()
    {
        if (mainButton != null) buttonRestPosition = mainButton.localPosition;
    }

    private void OnEnable()
    {
        if (buttonRelay != null) buttonRelay.CollisionEntered += HandleButtonCollision;
        PlayerActionController.RemotePunchPlayed += HandleRemotePunch;
        subscribeRoutine = StartCoroutine(SubscribeWhenReady());
    }

    private void Update()
    {
        RemoveUnavailableIngredients();
        if (state == OvenState.Waiting && Time.unscaledTime >= responseDeadline)
            BeginOpening();
        else if (state == OvenState.Cooking && Time.time >= cookingDeadline && !doorController.IsMoving)
            FinishCooking();
        else if (state == OvenState.EmptyPerformance && !doorController.IsMoving)
        {
            if (!emptyWaitStarted)
            {
                emptyWaitStarted = true;
                cookingDeadline = Time.time + emptyOvenWaitDuration;
            }
            if (Time.time >= cookingDeadline) FinishCooking();
        }
        else if (state == OvenState.Opening
            && (doorController == null || !doorController.IsMoving) && !IsButtonMoving())
        {
            state = OvenState.Idle;
            capturedIngredients.Clear();
            capturedPlayers.Clear();
            pendingIngredients.Clear();
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
        if (buttonRelay != null) buttonRelay.CollisionEntered -= HandleButtonCollision;
        PlayerActionController.RemotePunchPlayed -= HandleRemotePunch;
        if (subscribeRoutine != null) StopCoroutine(subscribeRoutine);
        subscribeRoutine = null;
        if (subscribedServer != null)
            subscribedServer.Router.OnEntityDestroyed -= HandleEntitiesDestroyed;
        subscribedServer = null;
        requestVersion++;
        buttonTween?.Kill();
        buttonTween = null;
        if (mainButton != null) mainButton.localPosition = buttonRestPosition;
        HideAllGauges();
        RestoreIngredients();
        currentIngredients.Clear();
        pendingIngredients.Clear();
        capturedIngredients.Clear();
        capturedPlayers.Clear();
        state = OvenState.Idle;
    }
    #endregion

    public void PressButton(PlayerBrain player)
    {
        if (player == null || PlayerSpawnManager.Instance == null) return;
        BeginButtonPress(PlayerSpawnManager.Instance.IsMine(player.PlayerId));
    }

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

    public void HandleCookStart(CookStartPacket packet)
    {
        if (doorController == null) return;
        bool alreadyPressed = state == OvenState.Waiting || state == OvenState.EmptyPerformance;
        requestVersion++;
        state = OvenState.Cooking;
        cookingDeadline = Time.time + Mathf.Max(0f, packet.CookingTimeMs / 1000f);
        if (!alreadyPressed) PlayButtonMotion();
        doorController.CloseDoor();
        foreach (IngredientReaction reaction in currentIngredients.Values)
        {
            if (reaction == null) continue;
            FreezeIngredient(reaction.Catchable);
            reaction.GaugeUI?.StartFill(Mathf.Max(0f, cookingDeadline - Time.time));
        }
        // TODO: 서버의 다른 내부 플레이어 지정 등록과 종료 후 연결 해제 반영 시 연결.
    }

    public void HandleCookPause(CookPausePacket packet)
    {
        // 오븐은 중단 불가. Pause로 문이나 잠금을 해제하지 않는다.
    }

    public void HandleCookComplete(CookCompletePacket packet)
    {
        if ((packet.CookType & IngredientState.Roasted) == 0) return;
        pendingIngredients.Remove(packet.IngredientEntityId);
        if (!currentIngredients.TryGetValue(packet.IngredientEntityId, out IngredientReaction reaction)
            || reaction == null) return;
        reaction.GaugeUI?.Hide();
        reaction.ApplyServerAction((IngredientAction)packet.CookType);
    }

    private IEnumerator SubscribeWhenReady()
    {
        yield return new WaitUntil(() => ServerManager.Instance != null);
        subscribedServer = ServerManager.Instance;
        subscribedServer.Router.OnEntityDestroyed += HandleEntitiesDestroyed;
        subscribeRoutine = null;
    }

    private void HandleButtonCollision(Collision collision)
    {
        if (collision.collider.transform.IsChildOf(transform)) return;
        if (collision.collider.GetComponentInParent<PlayerBrain>() != null) return;
        if (collision.relativeVelocity.sqrMagnitude < minimumCollisionSpeed * minimumCollisionSpeed) return;
        BeginButtonPress(Router != null && Router.IsRegistrar);
    }

    private void HandleRemotePunch(PlayerBrain player)
    {
        if (IsLocked || mainButton == null || player == null) return;
        Transform facing = player.FacingRoot != null ? player.FacingRoot : player.transform;
        Vector3 offset = mainButton.position - facing.position;
        if (offset.sqrMagnitude > player.InteractDistance * player.InteractDistance) return;
        if (Vector3.Dot(facing.forward, offset.normalized) < 0.5f) return;
        // 추정 입력은 버튼만 잠근다. 수합·문 모션·빈 오븐 동작은 실행하지 않는다.
        BeginPendingLock(false);
    }

    private void BeginButtonPress(bool sendsRequest)
    {
        if (IsLocked || IsButtonMoving()) return;
        if (mainButton == null || innerTrigger == null || doorController == null)
        {
            Debug.LogError("Assign MainBtn, InnerTrigger and OvenDoorController.", this);
            return;
        }
        CaptureContents();
        BeginPendingLock();
        // 빈 오븐은 서버 시작 통지 없이도 감지한 클라에서 동작한다.
        if (pendingIngredients.Count == 0 && capturedPlayers.Count == 0)
        {
            state = OvenState.EmptyPerformance;
            emptyWaitStarted = false;
            doorController.CloseDoor();
            return;
        }
        if (!sendsRequest || !IsRegistered || ServerManager.Instance == null) return;
        SendStartRequests(requestVersion).Forget();
    }

    private void BeginPendingLock(bool playButtonMotion = true)
    {
        state = OvenState.Waiting;
        responseDeadline = Time.unscaledTime + startResponseTimeout;
        requestVersion++;
        if (playButtonMotion) PlayButtonMotion();
    }

    private void CaptureContents()
    {
        capturedIngredients.Clear();
        capturedPlayers.Clear();
        pendingIngredients.Clear();
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
            if (CanRoast(entity)) pendingIngredients.Add(entity.NetworkId);
        }
    }

    private static bool CanRoast(CatchableObj entity)
    {
        if (entity.Data is Ingredient data && (data.conditionFlag & (int)IngredientAction.Cook) != 0)
            return false;
        return !entity.TryGetComponent(out IngredientReaction reaction)
            || !reaction.IsActionCompleted(IngredientAction.Cook);
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
            if (isActiveAndEnabled && version == requestVersion) BeginOpening();
        }
    }

    private void PlayButtonMotion()
    {
        if (mainButton == null || IsButtonMoving()) return;
        buttonTween = DOTween.Sequence()
            .Append(mainButton.DOLocalMoveZ(pressedButtonZ, buttonPressDuration).SetEase(Ease.OutSine))
            .Append(mainButton.DOLocalMoveZ(buttonRestPosition.z, buttonReturnDuration).SetEase(Ease.InSine))
            .OnComplete(() => buttonTween = null);
    }

    private bool IsButtonMoving() => buttonTween != null && buttonTween.IsActive();

    private void FinishCooking()
    {
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

    private void FreezeIngredient(CatchableObj entity)
    {
        if (entity == null || frozenIngredients.ContainsKey(entity.NetworkId)) return;
        frozenIngredients[entity.NetworkId] = new FrozenIngredient
        {
            Entity = entity, CanBePicked = entity.CanBePicked,
            ColliderEnabled = entity.Col != null && entity.Col.enabled,
            IsKinematic = entity.Rb != null && entity.Rb.isKinematic,
            UseGravity = entity.Rb != null && entity.Rb.useGravity,
            Position = entity.transform.position,
            Rotation = entity.transform.rotation
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
            entity.Rb.isKinematic = frozen.IsKinematic;
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
            entity.transform.SetPositionAndRotation(frozen.Position, frozen.Rotation);
            if (!physicsTick || entity.Rb == null) continue;
            // 자체 이동 트레잇의 MovePosition도 조리 중에는 고정 위치로 덮어쓴다.
            entity.Rb.isKinematic = true;
            entity.Rb.MovePosition(frozen.Position);
            entity.Rb.MoveRotation(frozen.Rotation);
        }
    }

    private void HandleEntitiesDestroyed(IReadOnlyList<EntityDestroyPacket> packets)
    {
        foreach (EntityDestroyPacket packet in packets) RemoveIngredient(packet.EntityId, false);
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
        pendingIngredients.Remove(id);
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
}
