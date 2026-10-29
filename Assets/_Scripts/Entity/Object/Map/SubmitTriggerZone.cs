using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Protocol;
using Server;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class SubmitTriggerZone : MapObjInteraction, IEntityParentReceiver
{
    public enum SubmissionSurface { Pedestal, FrontWall, Auto }
    public enum FrontFace { PositiveZ, NegativeZ, PositiveX, NegativeX, PositiveY, NegativeY }
    [Tooltip("Auto: 트리거는 받침대, 일반 콜라이더는 정면 벽으로 사용합니다.")]
    [SerializeField] private SubmissionSurface surface = SubmissionSurface.Auto;
    [Header("Server-confirmed Intake and Ejection")]
    [SerializeField] private Transform suctionPoint;
    [SerializeField] private Transform returnPoint;
    [SerializeField, Min(0.01f)] private float suctionDuration = 0.3f;
    [Tooltip("Return Point 기준 로컬 방향. 비어 있는 정면 벽은 Front Face를 +Z(전방)로 사용합니다.")]
    [SerializeField] private Vector3 ejectDirection = new(0f, 1f, 1f);
    [SerializeField, Min(0f)] private float ejectForce = 3f;
    [Tooltip("플레이어 쪽을 향한 BoxCollider 면을 선택하세요. 오브젝트의 로컬 축 기준입니다.")]
    [SerializeField] private FrontFace frontFace;
    [SerializeField] private bool logDiagnostics = true;
    [SerializeField] private string lastStatus;
    private readonly Dictionary<long, string> reportedBlocks = new();
    [SerializeField, Min(0.01f)] private float stationarySeconds = 0.5f;
    [Tooltip("정지 인정 최대 선속도(m/s)")]
    [SerializeField, Min(0f)] private float linearSpeedTolerance = 0.01f;
    [Tooltip("정지 인정 최대 각속도(rad/s)")]
    [SerializeField, Min(0f)] private float angularSpeedTolerance = 0.01f;
    [Tooltip("물리 스텝 사이 최대 위치 변화(m)")]
    [SerializeField, Min(0f)] private float positionTolerance = 0.001f;
    [Tooltip("물리 스텝 사이 최대 회전 변화(도)")]
    [SerializeField, Min(0f)] private float rotationTolerance = 0.1f;
    [Tooltip("선택한 정면과 접촉점 사이 허용 거리(로컬 단위)")]
    [SerializeField, Min(0f)] private float frontFaceTolerance = 0.01f;
    [SerializeField, Min(0.1f)] private float responseTimeout = 3f;
    [SerializeField, Min(0f)] private float ejectInterval = 0.15f;
    [SerializeField, Min(0f)] private float ejectRandomRangeX = 0.3f;
    [SerializeField] private float ejectRandomRangeYMin = -0.05f;
    [SerializeField] private float ejectRandomRangeYMax = 0.2f;
    [SerializeField, Min(0f)] private float ejectRandomRangeZ = 0.3f;
    private readonly Dictionary<long, SubmittedItem> trackedItems = new();
    private readonly Dictionary<long, ReturnedPlateBlock> returnedPlateBlocks = new();
    private readonly HashSet<long> queuedEjections = new();
    private readonly Dictionary<long, int> ejectSeeds = new();
    private readonly List<long> staleIds = new();
    private ObjectNetworkRouter objectRouter;
    private CookingSubmitEjector ejector;
    private CookingSubmitEjector Ejector => ejector ??= new CookingSubmitEjector(this);
    public override Define.eToolId ToolType => Define.eToolId.CookingSubmit;
    public override EntityCategory AcceptedCategories => EntityCategory.Plate | EntityCategory.Ingredient;
    internal float ResponseTimeout => responseTimeout;
    private BoxCollider zone;
    private SubmissionSurface ActiveSurface => surface == SubmissionSurface.Auto
        ? (zone.isTrigger ? SubmissionSurface.Pedestal : SubmissionSurface.FrontWall) : surface;
    private readonly Dictionary<Collider, CatchableObj> overlaps = new();
    private readonly Dictionary<CatchableObj, RestSample> samples = new();
    private readonly HashSet<CatchableObj> touching = new();
    private readonly List<Collider> staleColliders = new();
    private readonly List<CatchableObj> staleItems = new();
    // Preserve contact order, then send one Interact per station after its Insert requests.
    private static readonly List<SubmissionRequest> pending = new();
    private static bool sending;
    private static readonly List<SubmitTriggerZone> activeZones = new();
    private static int requestGeneration;

    // Stop new submissions while allowing server-confirmed returns to finish.
    public static void StopUnsentRequests()
    {
        requestGeneration++;
        foreach (SubmissionRequest request in pending)
            if (request.Owner != null) request.Owner.CancelUnsent(request.EntityId);
        pending.Clear();
    }

    public static void ResetAllClientStageState()
    {
        requestGeneration++;
        pending.Clear();
        foreach (SubmitTriggerZone zone in activeZones)
            if (zone != null) zone.ResetClientStage();
    }

    private sealed class SubmittedItem
    {
        public CatchableObj Item;
        public long Id;
        public Quaternion OriginalRotation;
        public bool CanBePicked;
        public bool Accepted;
        public bool SuctionComplete;
        public bool Ejecting;
        public bool Returned;
        public float Deadline;
        public float ReleaseAfter;
        public Coroutine Motion;
        public readonly Dictionary<Collider, bool> Colliders = new();
        public readonly List<(Collider item, Collider station)> IgnoredCollisions = new();
    }
    private sealed class RestSample
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public CatchableObj Food;
        public double Since = double.NaN;
        public bool HasPose;
        public void Reset() { Since = double.NaN; HasPose = false; }
    }
    private sealed class ReturnedPlateBlock
    {
        public CatchableObj Item;
        public Action Clear;
    }
    private struct SubmissionRequest
    {
        public CatchableObj Item;
        public long EntityId;
        public double QualifiedAt;
        public SubmitTriggerZone Owner;
        public long ToolEntityId;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetQueue()
    {
        pending.Clear(); sending = false;
        requestGeneration = 0;
        activeZones.Clear();
    }
    private void Awake() => zone = GetComponent<BoxCollider>();
    private void OnEnable()
    {
        if (zone == null) zone = GetComponent<BoxCollider>();
        if (!activeZones.Contains(this)) activeZones.Add(this);
        if (surface != SubmissionSurface.Auto)
            zone.isTrigger = surface == SubmissionSurface.Pedestal;
        lastStatus = $"대기: {ActiveSurface}, 정면={frontFace}, IsTrigger={zone.isTrigger}";
        if (logDiagnostics) Debug.Log($"[Submit Ready] {name}: {lastStatus}", this);
    }
    private void OnDisable()
    {
        activeZones.Remove(this);
        ResetClientStage();
    }
    private static CatchableObj GetSubmissionItem(Collider other)
    {
        // Contact with food on a plate submits the outer plate, not its child ingredient.
        CatchableObj item = null;
        foreach (CatchableObj parent in other.GetComponentsInParent<CatchableObj>()) item = parent;
        return item != null && (item.Category == EntityCategory.Ingredient ||
            item.TryGetComponent<PlateInteraction>(out _)) ? item : null;
    }
    private void OnTriggerEnter(Collider other)
    {
        if (ActiveSurface != SubmissionSurface.Pedestal) return;
        CatchableObj item = GetSubmissionItem(other);
        if (item != null)
        {
            overlaps[other] = item;
            lastStatus = $"받침대 접촉: {item.name} ({item.NetworkId})";
        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (!overlaps.TryGetValue(other, out CatchableObj item)) return;
        overlaps.Remove(other);
        if (!overlaps.ContainsValue(item)) RemoveSample(item);
    }
    private void OnTriggerStay(Collider other) => OnTriggerEnter(other);
    private void RemoveSample(CatchableObj item)
    {
        if (!samples.TryGetValue(item, out RestSample sample)) return;
        if (item != null) item.OnPicked -= sample.Reset;
        samples.Remove(item);
    }
    private void FixedUpdate()
    {
        staleIds.Clear();
        foreach (var pair in returnedPlateBlocks)
            if (!IsSameItem(pair.Value.Item, pair.Key)) staleIds.Add(pair.Key);
        foreach (long id in staleIds) ClearReturnedPlateBlock(id);
        UpdatePedestalSamples();
        staleIds.Clear();
        foreach (var pair in trackedItems)
        {
            SubmittedItem tracked = pair.Value;
            if (!IsSameItem(tracked.Item, pair.Key) ||
                (!tracked.Accepted && Time.unscaledTime >= tracked.Deadline) ||
                (tracked.Returned && Time.fixedTime >= tracked.ReleaseAfter && !OverlapsEjectionGeometry(tracked)))
                staleIds.Add(pair.Key);
            else if (tracked.Accepted && !tracked.Returned)
                foreach (Collider col in tracked.Colliders.Keys)
                    if (col != null) col.enabled = false;
        }
        foreach (long id in staleIds) ForgetItem(id);
    }

    private void UpdatePedestalSamples()
    {
        if (ActiveSurface != SubmissionSurface.Pedestal) return;
        touching.Clear();
        staleColliders.Clear();
        foreach (var pair in overlaps)
        {
            if (pair.Key == null || !pair.Key.enabled || !pair.Key.gameObject.activeInHierarchy ||
                pair.Value == null || !pair.Value.gameObject.activeInHierarchy)
                staleColliders.Add(pair.Key);
            else touching.Add(pair.Value);
        }
        foreach (Collider stale in staleColliders) overlaps.Remove(stale);
        staleItems.Clear();
        foreach (var pair in samples)
            if (!touching.Contains(pair.Key)) staleItems.Add(pair.Key);
        foreach (CatchableObj item in staleItems) RemoveSample(item);
        double now = Time.unscaledTimeAsDouble;
        foreach (CatchableObj item in touching)
        {
            if (!samples.TryGetValue(item, out RestSample sample))
            {
                sample = new RestSample();
                samples.Add(item, sample);
                item.OnPicked += sample.Reset;
            }
            if (!CheckEligibility(item, false) || item.Rb == null) { sample.Reset(); continue; }
            CatchableObj food = item.TryGetComponent(out PlateInteraction plate) ? plate.CurrentFood : null;
            bool still = item.LinearVelocity.sqrMagnitude <= linearSpeedTolerance * linearSpeedTolerance &&
                item.Rb.angularVelocity.sqrMagnitude <= angularSpeedTolerance * angularSpeedTolerance;
            if (sample.HasPose)
                still &= (item.transform.position - sample.Position).sqrMagnitude <= positionTolerance * positionTolerance &&
                    Quaternion.Angle(item.transform.rotation, sample.Rotation) <= rotationTolerance && sample.Food == food;
            sample.Position = item.transform.position;
            sample.Rotation = item.transform.rotation;
            sample.Food = food;
            sample.HasPose = true;
            if (!still) { sample.Since = double.NaN; continue; }
            if (double.IsNaN(sample.Since)) sample.Since = now;
            // Stationary plates and ingredients wait for the bell before submission.
        }
    }

    private void OnBellRung()
    {
        if (ActiveSurface != SubmissionSurface.Pedestal) return;
        UpdatePedestalSamples();
        double now = Time.unscaledTimeAsDouble;
        int submitted = 0;
        foreach (var pair in samples)
        {
            if (double.IsNaN(pair.Value.Since) || now - pair.Value.Since < stationarySeconds ||
                !CheckEligibility(pair.Key, false)) continue;
            // One bell press inserts all ready entities, then interacts with this station.
            Enqueue(pair.Key, now, false);
            if (pair.Key.IsSubmissionPending) submitted++;
        }
        // A previous Insert may have succeeded even if its Interact send was interrupted.
        RoomHudState state = RoomHudSession.Instance.State;
        if (submitted == 0 && IsRegistered && state.IsStageRunning && state.RemainingSeconds(now) > 0)
        {
            foreach (var pair in trackedItems)
            {
                if (!pair.Value.Accepted || pair.Value.Returned || queuedEjections.Contains(pair.Key)) continue;
                pending.Add(new SubmissionRequest { Owner = this, ToolEntityId = NetworkId, QualifiedAt = now });
                break;
            }
        }
        lastStatus = $"종 일괄 제출: {submitted}개 (정지 {stationarySeconds:0.##}초 이상)";
        if (logDiagnostics) Debug.Log($"[Submit Bell] {lastStatus}", this);
    }
    private void OnCollisionEnter(Collision collision) => CheckFrontContact(collision);
    private void OnCollisionStay(Collision collision) => CheckFrontContact(collision);
    private void CheckFrontContact(Collision collision)
    {
        if (ActiveSurface != SubmissionSurface.FrontWall || zone.isTrigger) return;
        CatchableObj item = GetSubmissionItem(collision.collider);
        if (!CheckEligibility(item)) return;
        Vector3 axis = FrontAxis;
        Vector3 worldAxis = transform.TransformDirection(axis).normalized;
        Vector3 half = zone.size * 0.5f;
        float extent = Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y + Mathf.Abs(axis.z) * half.z;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.thisCollider != zone && contact.otherCollider != zone) continue;
            Vector3 point = transform.InverseTransformPoint(contact.point);
            // Position selects the configured side; the normal excludes adjacent faces.
            if (Mathf.Abs(Vector3.Dot(point - zone.center, axis) - extent) > frontFaceTolerance ||
                Mathf.Abs(Vector3.Dot(contact.normal, worldAxis)) < 0.9f) continue;
            lastStatus = $"정면 접촉: {item.name} ({item.NetworkId})";
            Enqueue(item, Time.unscaledTimeAsDouble);
            return;
        }
        ReportBlocked(item, $"정면 외 접촉: Front Face={frontFace}. 플레이어를 향한 로컬 면인지 확인하세요.");
    }
    private Vector3 FrontAxis => frontFace switch
    {
        FrontFace.NegativeZ => Vector3.back,
        FrontFace.PositiveX => Vector3.right,
        FrontFace.NegativeX => Vector3.left,
        FrontFace.PositiveY => Vector3.up,
        FrontFace.NegativeY => Vector3.down,
        _ => Vector3.forward
    };

    private void OnDrawGizmosSelected()
    {
        BoxCollider box = zone != null ? zone : GetComponent<BoxCollider>();
        if (box == null || (surface == SubmissionSurface.Auto && box.isTrigger) ||
            surface == SubmissionSurface.Pedestal) return;
        Vector3 axis = FrontAxis;
        Vector3 half = box.size * 0.5f;
        float extent = Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y + Mathf.Abs(axis.z) * half.z;
        Vector3 origin = transform.TransformPoint(box.center + axis * extent);
        Vector3 direction = transform.TransformDirection(axis).normalized;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + direction * 0.5f);
        Gizmos.DrawWireSphere(origin + direction * 0.5f, 0.04f);
    }

    private string SubmissionBlock(CatchableObj item, bool requireLocalOwner = true)
    {
        if (item == null) return "제출할 그릇 또는 재료가 아닙니다.";
        if (!item.gameObject.activeInHierarchy) return "비활성 접시";
        if (item.IsSubmissionPending || item.IsSubmissionReturning) return "이미 제출 요청 또는 반환 중";
        if (item.IsHold || item.Holder != null) return "손에 든 접시";
        if (returnedPlateBlocks.ContainsKey(item.NetworkId)) return "배출한 접시: 플레이어가 다시 집을 때까지 재흡입 금지";
        if (requireLocalOwner && !item.IsLocalOwner &&
            (!string.IsNullOrEmpty(item.LastHolderPlayerId) || Router == null || !Router.IsRegistrar))
            return "이 클라이언트에서 요청할 수 없는 물체";
        if (!IsRegistered || objectRouter == null) return "제출구 도구 등록 대기 중 (ToolId=100)";
        if (trackedItems.ContainsKey(item.NetworkId)) return "이미 제출구에서 처리 중";
        if (item.NetworkId <= 0) return "서버 엔티티 ID 없음";
        if (item.ParentEntityId > 0) return $"다른 도구에 들어 있는 접시: Parent={item.ParentEntityId}";
        // Empty plates and loose ingredients are valid intake targets; the server judges dishes.
        RoomHudState state = RoomHudSession.Instance.State;
        if (!state.IsStageRunning) return "스테이지 시작 알림 미수신 또는 이미 종료됨";
        return state.RemainingSeconds(Time.unscaledTimeAsDouble) <= 0 ? "스테이지 타이머 0초" : null;
    }
    private bool CheckEligibility(CatchableObj item, bool requireLocalOwner = true)
    {
        string reason = SubmissionBlock(item, requireLocalOwner);
        if (reason == null) return true;
        if (item != null && !item.IsSubmissionPending) ReportBlocked(item, reason);
        return false;
    }
    private void ReportBlocked(CatchableObj item, string reason)
    {
        lastStatus = $"{item.name} ({item.NetworkId}): {reason}";
        if (!logDiagnostics || (reportedBlocks.TryGetValue(item.NetworkId, out string previous) && previous == reason)) return;
        reportedBlocks[item.NetworkId] = reason;
        Debug.Log($"[Submit Blocked] {lastStatus}", this);
    }
    private void Enqueue(CatchableObj item, double qualifiedAt, bool requireLocalOwner = true)
    {
        if (ServerManager.Instance == null || SubmissionBlock(item, requireLocalOwner) != null) return;
        SubmittedItem tracked = Track(item);
        if (!item.TryBeginSubmission()) { ForgetItem(item.NetworkId); return; }
        pending.Add(new SubmissionRequest
        {
            Item = item, EntityId = item.NetworkId, QualifiedAt = qualifiedAt,
            Owner = this, ToolEntityId = NetworkId
        });
        Debug.Log($"[Submit Queued] EntityId={item.NetworkId}, conditionTime={qualifiedAt:F3}", item);
    }
    private void LateUpdate()
    {
        if (!sending && pending.Count > 0) SendPendingAsync().Forget();
    }

    private static async UniTask SendPendingAsync()
    {
        int generation = requestGeneration;
        sending = true;
        var stations = new Dictionary<SubmitTriggerZone, long>();
        try
        {
            while (generation == requestGeneration && pending.Count > 0)
            {
                pending.Sort((a, b) =>
                {
                    int order = a.QualifiedAt.CompareTo(b.QualifiedAt);
                    return order != 0 ? order : a.EntityId.CompareTo(b.EntityId);
                });
                SubmissionRequest request = pending[0];
                pending.RemoveAt(0);
                SubmitTriggerZone owner = request.Owner;
                if (owner == null || !owner.isActiveAndEnabled || owner.NetworkId != request.ToolEntityId) continue;
                if (request.EntityId == 0)
                {
                    stations[owner] = request.ToolEntityId;
                    continue;
                }
                if (!IsSameItem(request.Item, request.EntityId) || !request.Item.IsSubmissionPending) continue;
                try
                {
                    if (ServerManager.Instance == null) throw new InvalidOperationException("ServerManager missing.");
                    DishOrderManager.ReportSubmission(request.Item.GetComponent<PlateInteraction>(), request.EntityId);
                    await ServerManager.Instance.SendData(PacketSerializer.Serialize(new EntityInsertPacket
                    {
                        SubjectEntityId = request.EntityId, TargetEntityId = request.ToolEntityId
                    }));
                    stations[owner] = request.ToolEntityId;
                }
                catch (Exception error)
                {
                    // An ambiguous send is never retried automatically.
                    DishOrderManager.ReportSubmissionIssue("제출구 삽입 전송을 확인할 수 없습니다.");
                    Debug.LogException(error);
                }
            }
            foreach (var station in stations)
            {
                if (generation != requestGeneration) break;
                if (station.Key == null || !station.Key.isActiveAndEnabled || station.Key.NetworkId != station.Value) continue;
                try
                {
                    await ServerManager.Instance.SendData(PacketSerializer.Serialize(new EntityInteractPacket
                    {
                        TargetEntityId = station.Value
                    }));
                    if (station.Key.logDiagnostics) Debug.Log($"[Submit TX] C_EntityInteract ToolEntityId={station.Value}", station.Key);
                }
                catch (Exception error)
                {
                    DishOrderManager.ReportSubmissionIssue("제출 처리 전송을 확인할 수 없습니다.");
                    Debug.LogException(error);
                }
            }
        }
        finally { sending = false; }
    }

    public override void InitializeRouter(MapObjNetworkRouter router)
    {
        base.InitializeRouter(router);
        objectRouter = router != null ? router.GetComponent<ObjectNetworkRouter>() : null;
        if (objectRouter == null) objectRouter = FindFirstObjectByType<ObjectNetworkRouter>();
    }

    public void HandleEntityAdded(CatchableObj item)
    {
        if (item == null) return;
        if (trackedItems.TryGetValue(item.NetworkId, out SubmittedItem previous) && previous.Returned) ForgetItem(item.NetworkId);
        SubmittedItem tracked = Track(item);
        if (tracked.Accepted) return;
        tracked.Accepted = true;
        foreach (Collider col in item.GetComponentsInChildren<Collider>(true))
        {
            if (col.GetComponentInParent<CatchableObj>() != item) continue;
            tracked.Colliders[col] = col.enabled;
        }
        item.SetSubmissionReturning(true);
        item.ChangePickState(false);
        item.SetPhysicsState(false);
        foreach (Collider col in tracked.Colliders.Keys)
            if (col != null) col.enabled = false;
        item.transform.SetParent(transform, true);
        tracked.Motion = StartCoroutine(SuckItem(tracked));
    }

    // The server can clear parents before sending its ejection packet.
    public void HandleEntityRemoved(CatchableObj item) { }

    public void HandleDestroyedObj(CatchableObj item)
    {
        if (item != null) ForgetItem(item.NetworkId);
    }

    public void HandleEject(CookingSubmitEjectPacket packet)
    {
        if (packet == null || packet.CookingSubmitEntityId != NetworkId || packet.EntityIds == null || !isActiveAndEnabled) return;
        var targets = new List<long>(packet.EntityIds.Count);
        bool hasTargets = false;
        foreach (long id in packet.EntityIds)
        {
            // Keep an empty slot for duplicates so every client consumes the same random sequence.
            if (id <= 0 || (ejectSeeds.TryGetValue(id, out int seed) && seed == packet.EjectSeed) || !queuedEjections.Add(id))
            {
                targets.Add(0);
                continue;
            }
            ejectSeeds[id] = packet.EjectSeed;
            targets.Add(id);
            hasTargets = true;
            PrepareReturn(id);
        }
        if (hasTargets) Ejector.Enqueue(targets, packet.EjectSeed);
    }

    internal CatchableObj FindItem(long id) =>
        objectRouter != null && objectRouter.TryGet(id, out CatchableObj item) ? item : null;

    internal bool PrepareReturn(long id)
    {
        CatchableObj item = FindItem(id);
        if (!IsSameItem(item, id)) return false;
        if (item.Holder != null || (item.ParentEntityId > 0 && item.ParentEntityId != NetworkId))
            objectRouter.DetachForContainerEjection(item);
        HandleEntityAdded(item);
        objectRouter.BeginContainerEjection(item, NetworkId);
        queuedEjections.Add(id);
        return CanEject(id);
    }

    internal bool CanEject(long id) => trackedItems.TryGetValue(id, out SubmittedItem tracked) &&
        IsSameItem(tracked.Item, id) && tracked.SuctionComplete && !tracked.Ejecting && !tracked.Returned &&
        (!tracked.Item.TryGetComponent(out PlateInteraction plate) || plate.IsEmpty);

    internal void StartEjection(CatchableObj item, Vector3 direction)
    {
        if (item == null || !CanEject(item.NetworkId) ||
            !trackedItems.TryGetValue(item.NetworkId, out SubmittedItem tracked)) return;
        tracked.Ejecting = true;
        if (tracked.Motion != null) StopCoroutine(tracked.Motion);
        tracked.Motion = null;
        EjectItem(tracked, direction);
    }

    internal void SkipReturn(long id)
    {
        if (id <= 0) return;
        ForgetItem(id);
        Debug.LogWarning($"[Submit Return] 배출 대상 또는 음식 제거 확인 대기 초과: {id}", this);
    }

    internal void ConfigureEjection(ObjEjector.Settings settings)
    {
        settings.ejectPoint = returnPoint;
        settings.interval = ejectInterval;
        settings.baseDirection = returnPoint != null
            ? transform.InverseTransformDirection(returnPoint.TransformDirection(ejectDirection))
            : ActiveSurface == SubmissionSurface.FrontWall
                ? Quaternion.LookRotation(FrontAxis, Mathf.Abs(FrontAxis.y) > 0.9f ? Vector3.forward : Vector3.up) * ejectDirection
                : ejectDirection;
        settings.randomRangeX = ejectRandomRangeX;
        settings.randomRangeYMin = ejectRandomRangeYMin;
        settings.randomRangeYMax = ejectRandomRangeYMax;
        settings.randomRangeZ = ejectRandomRangeZ;
        settings.force = ejectForce;
    }

    private IEnumerator SuckItem(SubmittedItem tracked)
    {
        Vector3 intake = suctionPoint != null ? suctionPoint.position : zone.bounds.center;
        yield return MoveItem(tracked.Item, tracked.Id, intake, tracked.OriginalRotation, suctionDuration);
        if (IsSameItem(tracked.Item, tracked.Id)) tracked.SuctionComplete = true;
        tracked.Motion = null;
    }

    private void EjectItem(SubmittedItem tracked, Vector3 direction)
    {
        CatchableObj item = tracked.Item;
        if (!IsSameItem(item, tracked.Id)) return;
        // With no authored outlet, keep the exact final suction pose. Moving the plate
        // in front of the wall here causes a visible jump before the actual launch.
        if (returnPoint != null) SetItemPose(item, returnPoint.position, returnPoint.rotation);
        objectRouter.DetachForContainerEjection(item);
        // Register before restoring colliders and pickup so no new contact can resubmit it.
        BlockReturnedPlate(item);
        RestoreItem(tracked);
        IgnoreEjectionCollisions(tracked);
        if (item.Rb != null)
        {
            item.Rb.linearVelocity = Vector3.zero;
            item.Rb.angularVelocity = Vector3.zero;
            item.Rb.AddForce(direction * ejectForce, ForceMode.Impulse);
        }
        tracked.Returned = true;
        tracked.ReleaseAfter = Time.fixedTime + Time.fixedDeltaTime;
        tracked.Motion = null;
        queuedEjections.Remove(tracked.Id);
    }

    private void IgnoreEjectionCollisions(SubmittedItem tracked)
    {
        Transform stationRoot = transform.parent != null ? transform.parent : transform;
        Collider[] stationColliders = stationRoot.GetComponentsInChildren<Collider>();
        foreach (var entry in tracked.Colliders)
        {
            Collider itemCollider = entry.Key;
            if (!entry.Value || itemCollider == null || !itemCollider.enabled || itemCollider.isTrigger) continue;
            foreach (Collider stationCollider in stationColliders)
            {
                if (!stationCollider.enabled || stationCollider.isTrigger ||
                    stationCollider.GetComponentInParent<CatchableObj>() != null ||
                    stationCollider.GetComponentInParent<PlayerBrain>() != null ||
                    Physics.GetIgnoreCollision(itemCollider, stationCollider)) continue;
                Physics.IgnoreCollision(itemCollider, stationCollider, true);
                tracked.IgnoredCollisions.Add((itemCollider, stationCollider));
            }
        }
    }

    private bool OverlapsEjectionGeometry(SubmittedItem tracked)
    {
        if (tracked.IgnoredCollisions.Count == 0) return OverlapsItem(tracked.Item);
        foreach (var pair in tracked.IgnoredCollisions)
        {
            if (pair.item == null || pair.station == null || !pair.item.enabled || !pair.station.enabled ||
                !pair.item.gameObject.activeInHierarchy || !pair.station.gameObject.activeInHierarchy) continue;
            Bounds bounds = pair.item.bounds;
            bounds.Expand(0.04f);
            if (bounds.Intersects(pair.station.bounds)) return true;
        }
        return false;
    }

    private static void RestoreEjectionCollisions(SubmittedItem tracked)
    {
        foreach (var pair in tracked.IgnoredCollisions)
            if (pair.item != null && pair.station != null) Physics.IgnoreCollision(pair.item, pair.station, false);
        tracked.IgnoredCollisions.Clear();
    }

    private void BlockReturnedPlate(CatchableObj item)
    {
        if (!item.TryGetComponent<PlateInteraction>(out _)) return;
        long id = item.NetworkId;
        if (returnedPlateBlocks.ContainsKey(id)) return;
        var block = new ReturnedPlateBlock { Item = item, Clear = () => ClearReturnedPlateBlock(id) };
        returnedPlateBlocks.Add(id, block);
        item.OnPicked += block.Clear;
        item.OnDespawned += block.Clear;
    }

    private void ClearReturnedPlateBlock(long id)
    {
        if (!returnedPlateBlocks.TryGetValue(id, out ReturnedPlateBlock block)) return;
        returnedPlateBlocks.Remove(id);
        if (block.Item != null)
        {
            block.Item.OnPicked -= block.Clear;
            block.Item.OnDespawned -= block.Clear;
        }
        // Pickup may happen before the plate has left this station's collider.
        if (trackedItems.TryGetValue(id, out SubmittedItem tracked) &&
            tracked.Returned && tracked.Item == block.Item) ForgetItem(id);
        reportedBlocks.Remove(id);
    }

    private SubmittedItem Track(CatchableObj item)
    {
        if (!trackedItems.TryGetValue(item.NetworkId, out SubmittedItem tracked))
        {
            tracked = new SubmittedItem
            {
                Item = item, Id = item.NetworkId,
                OriginalRotation = item.transform.rotation, CanBePicked = item.CanBePicked,
                Deadline = Time.unscaledTime + responseTimeout
            };
            trackedItems.Add(item.NetworkId, tracked);
        }
        return tracked;
    }

    private void CancelUnsent(long id)
    {
        if (trackedItems.TryGetValue(id, out SubmittedItem tracked) && !tracked.Accepted) ForgetItem(id);
    }

    private void ForgetItem(long id)
    {
        if (trackedItems.TryGetValue(id, out SubmittedItem tracked))
        {
            if (tracked.Motion != null) StopCoroutine(tracked.Motion);
            RestoreEjectionCollisions(tracked);
            if (!tracked.Returned && IsSameItem(tracked.Item, id)) RestoreItem(tracked);
            trackedItems.Remove(id);
        }
        queuedEjections.Remove(id);
        objectRouter?.EndContainerEjection(id, NetworkId);
    }

    private static void RestoreItem(SubmittedItem tracked)
    {
        CatchableObj item = tracked.Item;
        if (tracked.Accepted)
        {
            item.transform.SetParent(null, true);
            Vector3 position = item.transform.position;
            Quaternion rotation = item.transform.rotation;
            item.SetPhysicsState(true);
            // Seed the dynamic body at the exact final animation pose before applying force.
            SetItemPose(item, position, rotation);
            foreach (var col in tracked.Colliders)
                if (col.Key != null) col.Key.enabled = col.Value;
        }
        item.ChangePickState(tracked.CanBePicked);
        item.ReleaseSubmissionLock();
        item.SetSubmissionReturning(false);
    }

    private bool OverlapsItem(CatchableObj item)
    {
        foreach (Collider col in item.GetComponentsInChildren<Collider>(true))
        {
            if (col.GetComponentInParent<CatchableObj>() != item || !col.gameObject.activeInHierarchy) continue;
            if (Physics.ComputePenetration(zone, zone.transform.position, zone.transform.rotation,
                col, col.transform.position, col.transform.rotation, out _, out _)) return true;
        }
        return false;
    }

    public void ResetClientStage()
    {
        pending.RemoveAll(request => request.Owner == this);
        ejector?.Reset();
        StopAllCoroutines();
        foreach (long id in new List<long>(trackedItems.Keys)) ForgetItem(id);
        foreach (long id in new List<long>(returnedPlateBlocks.Keys)) ClearReturnedPlateBlock(id);
        queuedEjections.Clear();
        ejectSeeds.Clear();
        foreach (var pair in samples)
            if (pair.Key != null) pair.Key.OnPicked -= pair.Value.Reset;
        samples.Clear();
        overlaps.Clear();
        reportedBlocks.Clear();
    }

    private static bool IsSameItem(CatchableObj item, long id) =>
        item != null && item.gameObject.activeInHierarchy && item.NetworkId == id;

    private static IEnumerator MoveItem(CatchableObj item, long id, Vector3 destination, Quaternion rotation, float duration)
    {
        Vector3 start = item.transform.position;
        Quaternion startRotation = item.transform.rotation;
        float elapsed = 0f;
        while (IsSameItem(item, id) && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            SetItemPose(item, Vector3.Lerp(start, destination, t), Quaternion.Slerp(startRotation, rotation, t));
            yield return null;
        }
        if (IsSameItem(item, id)) SetItemPose(item, destination, rotation);
    }

    private static void SetItemPose(CatchableObj item, Vector3 position, Quaternion rotation)
    {
        // Transform-only animation can leave PhysX/interpolation at a previous pose.
        if (item.Rb != null)
        {
            item.Rb.position = position;
            item.Rb.rotation = rotation;
        }
        item.transform.SetPositionAndRotation(position, rotation);
    }
}
