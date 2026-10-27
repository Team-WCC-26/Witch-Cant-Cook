using System;
using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

[MovedFrom(true, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "TrashCan")]
public sealed class TrashCanInteraction : MapObjInteraction, IEntityParentReceiver, IPunchReceiver, IClientStageResettable
{
    [Serializable]
    private sealed class EjectOverride
    {
        public CatchableObj target;
        public EntityCategory categories;
        public Vector3 direction = new(0f, 1f, 1f);
        [Min(0f)] public float force = 5f;
    }

    private sealed class TrackedObj
    {
        public CatchableObj obj;
        public bool accepted;
        public bool returned;
        public bool canBePicked;
        public float deadline;
        public float releaseAfter;
        public readonly Dictionary<Collider, bool> colliders = new();
    }

    [Header("Respawn Point")]
    public Transform kitchenSpawnPoint;
    [SerializeField] private Collider intakeTrigger;
    [SerializeField, Min(0.1f)] private float responseTimeout = 3f;
    [Header("Ejection")]
    [SerializeField] private Transform ejectPoint;
    [SerializeField, Min(0f)] private float ejectInterval = 0.15f;
    [SerializeField] private Vector3 ejectBaseDirection = new(0f, 1f, 1f);
    [SerializeField, Min(0f)] private float ejectRandomRangeX = 0.3f;
    [SerializeField] private float ejectRandomRangeYMin = -0.05f;
    [SerializeField] private float ejectRandomRangeYMax = 0.2f;
    [SerializeField, Min(0f)] private float ejectRandomRangeZ = 0.3f;
    [SerializeField, Min(0f)] private float ejectForce = 5f;
    [SerializeField, Min(0.1f)] private float ejectGizmoLength = 2f;
    [SerializeField] private List<EjectOverride> ejectOverrides = new();

    private readonly Dictionary<long, TrackedObj> trackedObjects = new();
    private readonly HashSet<long> queuedIds = new();
    private readonly Dictionary<string, PlayerBrain> insidePlayers = new();
    private readonly Dictionary<string, float> pendingPlayerEntries = new();
    private readonly Dictionary<string, float> pendingPlayerExits = new();
    private readonly List<long> removedIds = new();
    private readonly List<string> exitedPlayers = new();
    private ObjectNetworkRouter objectRouter;
    private TrashCanEjector ejector;
    private float nextPressTime;

    public override Define.eToolId ToolType => Define.eToolId.TrashCan;
    public override EntityCategory AcceptedCategories => EntityCategory.All;
    internal float ResponseTimeout => responseTimeout;
    private TrashCanEjector Ejector => ejector ??= new TrashCanEjector(this);

    #region Unity Callbacks

    private void Awake()
    {
        if (intakeTrigger == null)
            foreach (Collider candidate in GetComponents<Collider>())
                if (candidate.isTrigger) { intakeTrigger = candidate; break; }
    }

    private void OnTriggerEnter(Collider other) => TryAccept(other);
    private void OnTriggerStay(Collider other) => TryAccept(other);

    private void FixedUpdate()
    {
        removedIds.Clear();
        foreach (var entry in trackedObjects)
        {
            TrackedObj tracked = entry.Value;
            // 내용물 제거 콜백이 도구의 보조 Trigger를 켜더라도 보관 중에는 접수를 막는다.
            if (tracked.accepted && !tracked.returned)
                foreach (Collider col in tracked.colliders.Keys)
                    if (col != null) col.enabled = false;
            if (tracked.obj == null || tracked.obj.NetworkId != entry.Key)
                removedIds.Add(entry.Key);
            else if (tracked.returned && Time.fixedTime >= tracked.releaseAfter && !Overlaps(tracked.obj))
                removedIds.Add(entry.Key);
            else if (!tracked.accepted && (Time.unscaledTime >= tracked.deadline || !Overlaps(tracked.obj)))
                removedIds.Add(entry.Key);
        }
        foreach (long id in removedIds) ForgetObj(id);

        exitedPlayers.Clear();
        foreach (var entry in insidePlayers)
            if (entry.Value == null || !Overlaps(entry.Value)) exitedPlayers.Add(entry.Key);
        foreach (string playerId in exitedPlayers) RequestPlayerExit(playerId);
        ExpirePlayerRequests(pendingPlayerEntries);
        ExpirePlayerRequests(pendingPlayerExits);
    }

    private void OnDisable() => ResetClientStage();

    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        Ejector.DrawGizmos();
#endif
    }

    #endregion

    public override void InitializeRouter(MapObjNetworkRouter router)
    {
        base.InitializeRouter(router);
        objectRouter = router != null ? router.GetComponent<ObjectNetworkRouter>() : null;
    }

    public void OnPunched(PlayerBrain player) => PressButton(player);

    public void PressButton(PlayerBrain player)
    {
        if (player == null || PlayerSpawnManager.Instance == null || !PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        if (NetworkId == 0 || ServerManager.Instance == null || Time.unscaledTime < nextPressTime) return;
        nextPressTime = Time.unscaledTime + 0.2f;
        EntityInteractPacket packet = new() { TargetEntityId = NetworkId };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void HandleEntityAdded(CatchableObj entity)
    {
        if (entity == null) return;
        TrackedObj tracked = Track(entity);
        if (tracked.accepted || tracked.returned) return;
        tracked.accepted = true;
        tracked.canBePicked = entity.CanBePicked;
        foreach (Collider col in entity.GetComponentsInChildren<Collider>(true))
        {
            if (col.GetComponentInParent<CatchableObj>() != entity) continue;
            tracked.colliders[col] = col.enabled;
            col.enabled = false;
        }
        entity.ChangePickState(false);
        entity.SetPhysicsState(false);
        entity.transform.SetParent(transform, true);
        entity.transform.position = intakeTrigger != null ? intakeTrigger.bounds.center : transform.position;
    }

    // 부모 해제는 배출 패킷보다 먼저 올 수 있다. 물리 복구는 배출기가 담당한다.
    public void HandleEntityRemoved(CatchableObj entity) { }

    public void HandleDestroyedObj(CatchableObj entity)
    {
        if (entity == null || !trackedObjects.TryGetValue(entity.NetworkId, out TrackedObj tracked)) return;
        RestoreColliders(tracked);
        ForgetObj(entity.NetworkId);
    }

    public void HandleEject(TrashCanEjectPacket packet)
    {
        if (packet == null) return;
        List<long> ids = new();
        foreach (long id in packet.EntityIds)
        {
            if (trackedObjects.TryGetValue(id, out TrackedObj tracked) && tracked.returned) continue;
            if (!queuedIds.Add(id)) continue;
            ids.Add(id);
            PrepareReturn(id);
        }
        if (ids.Count > 0) Ejector.Enqueue(ids, packet.EjectSeed, packet.EjectDelayMs);
    }

    public void HandlePlayerEnter(ToolPlayerEnterPacket packet)
    {
        pendingPlayerEntries.Remove(packet.PlayerId);
        if (PlayerSpawnManager.Instance != null && PlayerSpawnManager.Instance.TryGetPlayer(packet.PlayerId, out PlayerBrain player))
            insidePlayers[packet.PlayerId] = player;
    }

    public void HandlePlayerExit(ToolPlayerExitPacket packet)
    {
        insidePlayers.Remove(packet.PlayerId);
        pendingPlayerEntries.Remove(packet.PlayerId);
        pendingPlayerExits.Remove(packet.PlayerId);
    }

    public bool TryRespawnPlayer(string playerId)
    {
        if (!insidePlayers.ContainsKey(playerId) && !pendingPlayerEntries.ContainsKey(playerId)) return false;
        PlayerSpawnManager.Instance.RespawnPlayer(playerId, kitchenSpawnPoint);
        RequestPlayerExit(playerId);
        return true;
    }

    public void ResetClientStage()
    {
        ejector?.Reset();
        foreach (var entry in trackedObjects)
        {
            if (!entry.Value.returned && entry.Value.obj != null && entry.Value.obj.NetworkId == entry.Key)
                RestoreObj(entry.Value);
            objectRouter?.EndContainerEjection(entry.Key, NetworkId);
        }
        foreach (string playerId in new List<string>(insidePlayers.Keys)) RequestPlayerExit(playerId);
        foreach (string playerId in new List<string>(pendingPlayerEntries.Keys)) RequestPlayerExit(playerId);
        trackedObjects.Clear();
        queuedIds.Clear();
        insidePlayers.Clear();
        pendingPlayerEntries.Clear();
        pendingPlayerExits.Clear();
        nextPressTime = 0f;
    }

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

    internal CatchableObj FindObj(long id)
    {
        return objectRouter != null && objectRouter.TryGet(id, out CatchableObj obj) ? obj : null;
    }

    internal bool PrepareReturn(long id)
    {
        CatchableObj obj = FindObj(id);
        if (obj == null) return false;
        objectRouter.BeginContainerEjection(obj, NetworkId);
        if (obj.Holder != null) objectRouter.DetachForContainerEjection(obj);
        HandleEntityAdded(obj);
        return IsEmpty(obj);
    }

    internal bool CanEject(long id)
    {
        CatchableObj obj = FindObj(id);
        return obj != null && IsEmpty(obj);
    }

    internal void ReleaseObj(CatchableObj obj)
    {
        if (!trackedObjects.TryGetValue(obj.NetworkId, out TrackedObj tracked)) return;
        objectRouter.DetachForContainerEjection(obj);
        RestoreObj(tracked);
        tracked.returned = true;
        tracked.releaseAfter = Time.fixedTime + Time.fixedDeltaTime;
        queuedIds.Remove(obj.NetworkId);
    }

    internal void SkipReturn(long id)
    {
        if (FindObj(id) is CatchableObj obj) ReleaseObj(obj);
        else ForgetObj(id);
        Debug.LogError($"쓰레기통 배출 결과를 확인할 수 없습니다. EntityId: {id}", this);
    }

    internal Vector3 GetEjectDirection(CatchableObj obj) => FindOverride(obj)?.direction ?? ejectBaseDirection;
    internal float GetEjectForce(CatchableObj obj) => FindOverride(obj)?.force ?? ejectForce;

    private void TryAccept(Collider other)
    {
        if (NetworkId == 0 || objectRouter == null || ServerManager.Instance == null) return;
        CatchableObj contactedObj = other.GetComponentInParent<CatchableObj>();
        if (contactedObj != null && contactedObj.IsHold) return;
        PlayerBrain player = other.GetComponentInParent<PlayerBrain>();
        if (player != null) { RequestPlayerEntry(player); return; }
        // 내용물의 콜라이더가 먼저 닿아도 가장 바깥쪽 접시/팬을 접수한다.
        CatchableObj obj = null;
        foreach (CatchableObj parent in other.GetComponentsInParent<CatchableObj>()) obj = parent;
        if (obj == null || obj.NetworkId == 0 || obj.IsHold || trackedObjects.ContainsKey(obj.NetworkId)) return;
        if (obj.ParentEntityId != 0 && obj.ParentEntityId != -1) return;
        if (!obj.IsLocalOwner && !(string.IsNullOrEmpty(obj.LastHolderPlayerId) && Router != null && Router.IsRegistrar)) return;
        Track(obj);
        EntityInsertPacket packet = new() { SubjectEntityId = obj.NetworkId, TargetEntityId = NetworkId };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private TrackedObj Track(CatchableObj obj)
    {
        if (!trackedObjects.TryGetValue(obj.NetworkId, out TrackedObj tracked))
        {
            tracked = new TrackedObj { obj = obj, deadline = Time.unscaledTime + responseTimeout };
            trackedObjects.Add(obj.NetworkId, tracked);
        }
        return tracked;
    }

    private void ForgetObj(long id)
    {
        trackedObjects.Remove(id);
        queuedIds.Remove(id);
        objectRouter?.EndContainerEjection(id, NetworkId);
    }

    private static bool IsEmpty(CatchableObj obj)
    {
        return (!obj.TryGetComponent(out PlateInteraction plate) || plate.IsEmpty) &&
            (!obj.TryGetComponent(out PanInteraction pan) || !pan.HasIngredient);
    }

    private static void RestoreObj(TrackedObj tracked)
    {
        if (!tracked.accepted || tracked.obj == null) return;
        tracked.obj.transform.SetParent(null, true);
        tracked.obj.SetPhysicsState(true);
        tracked.obj.ChangePickState(tracked.canBePicked);
        RestoreColliders(tracked);
    }

    private static void RestoreColliders(TrackedObj tracked)
    {
        foreach (var entry in tracked.colliders)
            if (entry.Key != null) entry.Key.enabled = entry.Value;
    }

    private bool Overlaps(Component target)
    {
        if (intakeTrigger == null || target == null) return false;
        foreach (Collider col in target.GetComponentsInChildren<Collider>(true))
        {
            if (!col.gameObject.activeInHierarchy) continue;
            // 집어서 Collider가 꺼져도 실제 형상으로 완전 이탈을 확인한다.
            if (target is CatchableObj obj && col.GetComponentInParent<CatchableObj>() != obj) continue;
            if (target is PlayerBrain && col.GetComponentInParent<CatchableObj>() != null) continue;
            if (Physics.ComputePenetration(intakeTrigger, intakeTrigger.transform.position, intakeTrigger.transform.rotation,
                col, col.transform.position, col.transform.rotation, out _, out _)) return true;
        }
        return false;
    }

    private void RequestPlayerEntry(PlayerBrain player)
    {
        if (PlayerSpawnManager.Instance == null || !PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        if (insidePlayers.ContainsKey(player.PlayerId) || pendingPlayerExits.ContainsKey(player.PlayerId) || !pendingPlayerEntries.TryAdd(player.PlayerId, Time.unscaledTime + responseTimeout)) return;
        insidePlayers[player.PlayerId] = player;
        ToolPlayerEnterPacket packet = new() { ToolEntityId = NetworkId, PlayerId = player.PlayerId };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void RequestPlayerExit(string playerId)
    {
        if (PlayerSpawnManager.Instance == null || !PlayerSpawnManager.Instance.IsMine(playerId)) return;
        if (NetworkId == 0 || ServerManager.Instance == null || !pendingPlayerExits.TryAdd(playerId, Time.unscaledTime + responseTimeout)) return;
        ToolPlayerExitPacket packet = new() { ToolEntityId = NetworkId, PlayerId = playerId };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void ExpirePlayerRequests(Dictionary<string, float> requests)
    {
        exitedPlayers.Clear();
        foreach (var entry in requests)
            if (Time.unscaledTime >= entry.Value) exitedPlayers.Add(entry.Key);
        foreach (string playerId in exitedPlayers)
        {
            requests.Remove(playerId);
            insidePlayers.Remove(playerId);
        }
    }
    private EjectOverride FindOverride(CatchableObj obj)
    {
        if (obj == null) return null;
        foreach (EjectOverride entry in ejectOverrides)
            if (entry != null && entry.target == obj) return entry;
        foreach (EjectOverride entry in ejectOverrides)
            if (entry != null && entry.target == null && (entry.categories & obj.Category) != 0) return entry;
        return null;
    }
}
