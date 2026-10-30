using System;
using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;

public sealed class TrashCanInteraction : MapObjInteraction, IEntityParentReceiver, IClientStageResettable
{
    #region Intake Settings

    [Header("Intake")]
    [SerializeField] private Collider intakeTrigger;
    [SerializeField, Min(0.1f)] private float responseTimeout = 3f;

    #endregion

    #region Ejection Settings

    [Header("Ejection")]
    [SerializeField] private Transform ejectPoint;
    [SerializeField, Min(0f)] private float ejectInterval = 0.15f;
    [SerializeField, Min(0f)] private float ejectForce = 5f;
    [SerializeField] private List<ToolEjectSettings> toolEjectSettings = new();

    [Header("Ejection Direction")]
    [SerializeField] private Vector3 ejectBaseDirection = new(0f, 1f, 1f);
    [SerializeField, Min(0f)] private float ejectRandomRangeX = 0.3f;
    [SerializeField] private float ejectRandomRangeYMin = -0.05f;
    [SerializeField] private float ejectRandomRangeYMax = 0.2f;
    [SerializeField, Min(0f)] private float ejectRandomRangeZ = 0.3f;
    [SerializeField, Min(0.1f)] private float ejectGizmoLength = 2f;

    #endregion

    #region Player Settings

    [Header("Player Ejection")]
    [SerializeField, Min(0f)] private float playerEjectForce = 8f;

    [Header("Player Camera")]
    [SerializeField, Min(0f)] private float cameraDistanceOffset = 4f;
    [SerializeField, Range(0f, 89f)] private float cameraPitch = 65f;
    private PlayerCameraController localCameraController;
    private readonly List<Collider> overlapColliders = new();

    #endregion

    #region Object State

    private readonly Dictionary<long, TrackedObj> trackedObjects = new();
    private readonly HashSet<long> queuedIds = new();
    private readonly List<long> removedIds = new();

    #endregion

    #region Player State

    private readonly Dictionary<string, PlayerBrain> insidePlayers = new();
    private readonly Dictionary<string, float> pendingPlayerEntries = new();
    private readonly Dictionary<string, float> pendingPlayerExits = new();
    private readonly List<string> exitedPlayers = new();
    private readonly Dictionary<string, PlayerReturn> returningPlayers = new();

    #endregion

    #region Dependencies

    [SerializeField] private TrashCanAnimationController animationController;

    private ObjectNetworkRouter objectRouter;
    private TrashCanEjector ejector;
    private TrashCanEjector Ejector => ejector ??= new TrashCanEjector(this);

    #endregion

    #region Unity Callbacks

    private void Awake()
    {
        if (animationController == null) animationController = GetComponent<TrashCanAnimationController>();
        if (intakeTrigger != null) return;

        foreach (Collider candidate in GetComponents<Collider>())
        {
            if (!candidate.isTrigger) continue;

            intakeTrigger = candidate;
            break;
        }
    }

    private void OnTriggerEnter(Collider other) => TryAccept(other);
    private void OnTriggerStay(Collider other) => TryAccept(other);

    private void FixedUpdate()
    {
        UpdateLocalCameraView();
        removedIds.Clear();
        foreach (var entry in trackedObjects)
        {
            TrackedObj tracked = entry.Value;
            // Storage lock after content-removal callbacks
            if (tracked.accepted && !tracked.returned)
            {
                foreach (Collider col in tracked.colliders.Keys)
                    if (col != null) col.enabled = false;
            }

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

        exitedPlayers.Clear();
        foreach (var entry in returningPlayers)
        {
            PlayerReturn returning = entry.Value;
            if (returning.launched && Time.fixedTime >= returning.releaseAfter &&
                (returning.player == null || !Overlaps(returning.player)))
                exitedPlayers.Add(entry.Key);
        }
        foreach (string playerId in exitedPlayers) returningPlayers.Remove(playerId);
    }

    private void OnDisable() => ResetClientStage();

    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        Ejector.DrawGizmos();
#endif
    }

    #endregion

    #region Public API

    public override Define.eToolId ToolType => Define.eToolId.TrashCan;
    public Vector3 CameraTargetPos => intakeTrigger != null
        ? intakeTrigger.bounds.center + Vector3.up * intakeTrigger.bounds.extents.y
        : transform.position;
    public float CameraPitch => cameraPitch;
    public override EntityCategory AcceptedCategories => EntityCategory.None;

    public override void InitializeRouter(MapObjNetworkRouter router)
    {
        base.InitializeRouter(router);
        objectRouter = router != null ? router.GetComponent<ObjectNetworkRouter>() : null;
    }

    public void HandleEntityAdded(CatchableObj entity)
    {
        if (!StoreObj(entity)) return;

        // One processing sender per confirmed object entry
        if (Router != null && Router.IsRegistrar) RequestProcessing();
    }

    // Physics restoration deferred to ejection
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
        List<string> playerIds = new();
        foreach (string playerId in packet.PlayerIds)
        {
            if (returningPlayers.ContainsKey(playerId)) continue;
            returningPlayers.Add(playerId, new PlayerReturn());
            insidePlayers.Remove(playerId);
            pendingPlayerEntries.Remove(playerId);
            pendingPlayerExits.Remove(playerId);
            playerIds.Add(playerId);
        }
        if (ids.Count > 0 || playerIds.Count > 0)
            Ejector.Enqueue(ids, playerIds, packet.EjectSeed, packet.EjectDelayMs);
    }

    public void HandlePlayerEnter(ToolPlayerEnterPacket packet)
    {
        if (returningPlayers.ContainsKey(packet.PlayerId)) return;
        bool alreadyInside = insidePlayers.ContainsKey(packet.PlayerId) && !pendingPlayerEntries.ContainsKey(packet.PlayerId);
        pendingPlayerEntries.Remove(packet.PlayerId);
        if (!alreadyInside) animationController?.PlayIntake();
        if (PlayerSpawnManager.Instance != null && PlayerSpawnManager.Instance.TryGetPlayer(packet.PlayerId, out PlayerBrain player))
        {
            insidePlayers[packet.PlayerId] = player;
            if (PlayerSpawnManager.Instance.IsMine(packet.PlayerId)) RequestProcessing();
        }
    }

    public void HandlePlayerExit(ToolPlayerExitPacket packet)
    {
        insidePlayers.Remove(packet.PlayerId);
        pendingPlayerEntries.Remove(packet.PlayerId);
        pendingPlayerExits.Remove(packet.PlayerId);
    }

    public void ResetClientStage()
    {
        animationController?.ResetVisual();
        if (localCameraController != null) localCameraController.SetTrashCanView(this, 0f);
        localCameraController = null;
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
        returningPlayers.Clear();
    }

    #endregion

    #region Ejection

    internal float ResponseTimeout => responseTimeout;
    internal float PlayEjectionVisual() => animationController != null ? animationController.PlayEjection() : 0f;
    internal float EjectionLaunchDelay => animationController != null ? animationController.EjectionLaunchDelay : 0f;
    internal float PlayerEjectForce => playerEjectForce;

    internal PlayerBrain FindPlayer(string playerId)
    {
        PlayerBrain player = null;
        PlayerSpawnManager.Instance?.TryGetPlayer(playerId, out player);
        return player;
    }

    internal void ReleasePlayer(PlayerBrain player)
    {
        if (!returningPlayers.TryGetValue(player.PlayerId, out PlayerReturn returning)) return;
        returning.player = player;
        returning.launched = true;
        returning.releaseAfter = Time.fixedTime + Time.fixedDeltaTime;
    }

    internal void SkipPlayerReturn(string playerId)
    {
        returningPlayers.Remove(playerId);
        Debug.LogError($"Trash player ejection target not found: {playerId}", this);
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
        StoreObj(obj);
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

    internal Vector3 GetEjectDirection(CatchableObj obj) => FindToolEjectSettings(obj)?.direction ?? ejectBaseDirection;
    internal float GetEjectForce(CatchableObj obj) => FindToolEjectSettings(obj)?.force ?? ejectForce;

    private ToolEjectSettings FindToolEjectSettings(CatchableObj obj)
    {
        if (obj == null) return null;
        foreach (ToolEjectSettings entry in toolEjectSettings)
            if (entry != null && entry.target == obj) return entry;
        foreach (ToolEjectSettings entry in toolEjectSettings)
            if (entry != null && entry.target == null && (entry.categories & obj.Category) != 0) return entry;
        return null;
    }

    #endregion

    #region Processing

    private void RequestProcessing()
    {
        if (NetworkId == 0 || ServerManager.Instance == null) return;

        EntityInteractPacket packet = new() { TargetEntityId = NetworkId };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    #endregion

    #region Object Storage

    private bool StoreObj(CatchableObj entity)
    {
        if (entity == null) return false;

        TrackedObj tracked = Track(entity);
        if (tracked.accepted || tracked.returned) return false;

        tracked.accepted = true;
        animationController?.PlayIntake();
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
        return true;
    }

    private void TryAccept(Collider other)
    {
        if (NetworkId == 0 || objectRouter == null || ServerManager.Instance == null) return;
        CatchableObj contactedObj = other.GetComponentInParent<CatchableObj>();
        if (contactedObj != null && contactedObj.IsHold) return;

        PlayerBrain player = other.GetComponentInParent<PlayerBrain>();
        if (player != null)
        {
            RequestPlayerEntry(player);
            return;
        }

        // Outermost container, not its contents
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
        if (target is PlayerBrain player) return player.Body.Overlaps(intakeTrigger);
        target.GetComponentsInChildren(true, overlapColliders);
        foreach (Collider col in overlapColliders)
        {
            if (!col.gameObject.activeInHierarchy) continue;
            // Exit detection with disabled held colliders
            if (target is CatchableObj obj && col.GetComponentInParent<CatchableObj>() != obj) continue;
            if (col.enabled && !intakeTrigger.bounds.Intersects(col.bounds)) continue;
            if (Physics.ComputePenetration(intakeTrigger, intakeTrigger.transform.position, intakeTrigger.transform.rotation,
                col, col.transform.position, col.transform.rotation, out _, out _)) return true;
        }
        return false;
    }

    #endregion

    #region Player Tracking

    private void UpdateLocalCameraView()
    {
        PlayerSpawnManager spawnManager = PlayerSpawnManager.Instance;
        PlayerCameraController controller = null;
        if (spawnManager != null && !string.IsNullOrEmpty(spawnManager.MyID)
            && spawnManager.TryGetPlayer(spawnManager.MyID, out PlayerBrain player)
            && player != null && Overlaps(player))
            controller = player.CameraController;

        if (localCameraController != null && localCameraController != controller)
            localCameraController.SetTrashCanView(this, 0f);
        localCameraController = controller;
        if (localCameraController != null)
            localCameraController.SetTrashCanView(this, cameraDistanceOffset);
    }

    private void RequestPlayerEntry(PlayerBrain player)
    {
        if (PlayerSpawnManager.Instance == null || !PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        if (returningPlayers.ContainsKey(player.PlayerId)) return;
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

    #endregion

    #region Types

    private sealed class PlayerReturn
    {
        public PlayerBrain player;
        public bool launched;
        public float releaseAfter;
    }

    [Serializable]
    private sealed class ToolEjectSettings
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

    #endregion
}
