using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Protocol;
using Server;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class SubmitTriggerZone : MonoBehaviour
{
    public enum SubmissionSurface { Pedestal, FrontWall, Auto }
    public enum FrontFace { PositiveZ, NegativeZ, PositiveX, NegativeX, PositiveY, NegativeY }
    [Tooltip("Auto: 트리거는 받침대, 일반 콜라이더는 정면 벽으로 사용합니다.")]
    [SerializeField] private SubmissionSurface surface = SubmissionSurface.Auto;
    [Tooltip("이 받침대의 종. 비워 두면 기존 공통 Bell 이벤트를 사용합니다.")]
    [SerializeField] private Bell submissionBell;
    private Bell subscribedBell;
    private bool usingGlobalBell;
    [Header("Confirmed Plate Return")]
    [SerializeField] private Transform suctionPoint;
    [SerializeField] private Transform returnPoint;
    [SerializeField, Min(0.01f)] private float suctionDuration = 0.3f;
    [SerializeField, Min(0.01f)] private float ejectDuration = 0.2f;
    [Tooltip("Return Point 기준 로컬 방향. 비어 있으면 제출 영역 기준입니다.")]
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
    private BoxCollider zone;
    private SubmissionSurface ActiveSurface => surface == SubmissionSurface.Auto
        ? (zone.isTrigger ? SubmissionSurface.Pedestal : SubmissionSurface.FrontWall) : surface;
    private readonly Dictionary<Collider, CatchableObj> overlaps = new();
    private readonly Dictionary<CatchableObj, RestSample> samples = new();
    private readonly HashSet<CatchableObj> touching = new();
    private readonly List<Collider> staleColliders = new();
    private readonly List<CatchableObj> staleItems = new();
    // Local send order only. The server does not acknowledge acceptance yet.
    private static readonly List<SubmissionRequest> pending = new();
    private static bool sending;
    private static readonly List<SubmitTriggerZone> activeZones = new();
    private static readonly Dictionary<long, SubmitTriggerZone> submittingZones = new();
    private static int requestGeneration;

    // Server stop is the hard send cutoff. Keep sent dishes and their return animations alive.
    public static void StopUnsentRequests()
    {
        requestGeneration++;
        foreach (SubmissionRequest request in pending)
        {
            if (!IsSamePlate(request.Item, request.EntityId)) continue;
            request.Item.ReleaseSubmissionLock();
            submittingZones.Remove(request.EntityId);
        }
        pending.Clear();
    }

    public static void ResetAllClientStageState()
    {
        requestGeneration++;
        pending.Clear();
        submittingZones.Clear();
        foreach (SubmitTriggerZone zone in activeZones)
        {
            if (zone == null) continue;
            zone.StopAllCoroutines();
            foreach (var pair in zone.samples)
                if (pair.Key != null) pair.Key.OnPicked -= pair.Value.Reset;
            zone.samples.Clear();
            zone.overlaps.Clear();
            zone.reportedBlocks.Clear();
        }
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
    private struct SubmissionRequest
    {
        public CatchableObj Item;
        public long EntityId;
        public double QualifiedAt;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetQueue()
    {
        pending.Clear(); sending = false;
        requestGeneration = 0;
        activeZones.Clear(); submittingZones.Clear();
    }
    private void Awake() => zone = GetComponent<BoxCollider>();
    private void OnEnable()
    {
        if (zone == null) zone = GetComponent<BoxCollider>();
        if (!activeZones.Contains(this)) activeZones.Add(this);
        if (surface != SubmissionSurface.Auto)
            zone.isTrigger = surface == SubmissionSurface.Pedestal;
        if (ActiveSurface == SubmissionSurface.Pedestal)
        {
            subscribedBell = submissionBell;
            usingGlobalBell = subscribedBell == null;
            if (usingGlobalBell) Bell.OnBellRung += OnBellRung;
            else subscribedBell.Rung += OnBellRung;
        }
        lastStatus = $"대기: {ActiveSurface}, 정면={frontFace}, IsTrigger={zone.isTrigger}";
        if (logDiagnostics) Debug.Log($"[Submit Ready] {name}: {lastStatus}", this);
    }
    private void OnDisable()
    {
        activeZones.Remove(this);
        StopAllCoroutines();
        if (usingGlobalBell) Bell.OnBellRung -= OnBellRung;
        if (subscribedBell != null) subscribedBell.Rung -= OnBellRung;
        usingGlobalBell = false;
        subscribedBell = null;
        foreach (var pair in samples)
            if (pair.Key != null) pair.Key.OnPicked -= pair.Value.Reset;
        samples.Clear();
        overlaps.Clear();
        reportedBlocks.Clear();
    }
    private static CatchableObj GetPlate(Collider other)
    {
        // Do not treat a food child collider as direct contact with its plate.
        CatchableObj item = other.GetComponentInParent<CatchableObj>();
        return item != null && item.TryGetComponent<PlateInteraction>(out _) ? item : null;
    }
    private void OnTriggerEnter(Collider other)
    {
        if (ActiveSurface != SubmissionSurface.Pedestal) return;
        CatchableObj item = GetPlate(other);
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
    private void FixedUpdate() => UpdatePedestalSamples();

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
            CatchableObj food = item.GetComponent<PlateInteraction>().CurrentFood;
            bool still = item.Rb.linearVelocity.sqrMagnitude <= linearSpeedTolerance * linearSpeedTolerance &&
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
            // Being stationary makes this plate ready, but only ringing the bell submits it.
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
            // The ringer can request every ready plate, including the teammate's plates.
            // The existing server rejects already-destroyed dishes; no protocol change.
            Enqueue(pair.Key, now, false);
            if (pair.Key.IsSubmissionPending) submitted++;
        }
        lastStatus = $"종 일괄 제출: {submitted}개 (정지 {stationarySeconds:0.##}초 이상)";
        if (logDiagnostics) Debug.Log($"[Submit Bell] {lastStatus}", this);
    }
    private void OnCollisionEnter(Collision collision) => CheckFrontContact(collision);
    private void OnCollisionStay(Collision collision) => CheckFrontContact(collision);
    private void CheckFrontContact(Collision collision)
    {
        if (ActiveSurface != SubmissionSurface.FrontWall || zone.isTrigger) return;
        CatchableObj item = GetPlate(collision.collider);
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

    private static string SubmissionBlock(CatchableObj item, bool requireLocalOwner = true)
    {
        if (item == null) return "접시 콜라이더가 아닙니다.";
        if (!item.gameObject.activeInHierarchy) return "비활성 접시";
        if (item.IsSubmissionPending || item.IsSubmissionReturning) return "이미 제출 요청 또는 반환 중";
        if (item.IsHold || item.Holder != null) return "손에 든 접시";
        if (requireLocalOwner && !item.IsLocalOwner) return "이 클라이언트의 소유가 아닌 접시";
        if (item.NetworkId <= 0) return "서버 엔티티 ID 없음";
        if (item.ParentEntityId > 0) return $"다른 도구에 들어 있는 접시: Parent={item.ParentEntityId}";
        if (!item.TryGetComponent(out PlateInteraction plate) || plate.IsEmpty) return "빈 접시 또는 음식 정보 없음";
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
        if (ServerManager.Instance == null || SubmissionBlock(item, requireLocalOwner) != null || !item.TryBeginSubmission()) return;
        submittingZones[item.NetworkId] = this;
        pending.Add(new SubmissionRequest { Item = item, EntityId = item.NetworkId, QualifiedAt = qualifiedAt });
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
                CatchableObj item = request.Item;
                if (item == null || item.NetworkId != request.EntityId || !item.IsSubmissionPending) continue;
                try
                {
                    if (ServerManager.Instance == null) throw new InvalidOperationException("ServerManager missing.");
                    DishOrderManager.ReportSubmission(item.GetComponent<PlateInteraction>(), request.EntityId);
                    Debug.Log($"[Submit TX] C_ServeDish EntityId={request.EntityId}", item);
                    await ServerManager.Instance.SendData(PacketSerializer.Serialize(new ServeDishPacket { EntityId = request.EntityId }));
                    // Server removes the food and confirms return with S_DishEject.
                }
                catch (Exception error)
                {
                    // Delivery may already have happened. Never retry an ambiguous submission.
                    DishOrderManager.ReportSubmissionIssue("제출 전송을 확인할 수 없습니다. 중복 방지를 위해 자동 재전송하지 않습니다.");
                    Debug.LogException(error);
                }
            }
        }
        finally { sending = false; }
    }

    public static bool PlayConfirmedPlateReturn(CatchableObj plate)
    {
        if (plate == null || plate.IsSubmissionReturning) return false;
        submittingZones.TryGetValue(plate.NetworkId, out SubmitTriggerZone owner);
        submittingZones.Remove(plate.NetworkId);
        // Remote clients did not necessarily send this submission. Resolve the nearby station.
        if (owner == null || !owner.isActiveAndEnabled)
        {
            float closest = float.PositiveInfinity;
            foreach (SubmitTriggerZone candidate in activeZones)
            {
                if (candidate == null || !candidate.isActiveAndEnabled) continue;
                float distance = (candidate.zone.bounds.ClosestPoint(plate.transform.position) - plate.transform.position).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance;
                owner = candidate;
            }
        }
        if (owner == null) return false;
        owner.StartCoroutine(owner.ReturnPlate(plate));
        return true;
    }

    private IEnumerator ReturnPlate(CatchableObj item)
    {
        long id = item.NetworkId;
        Vector3 originalPosition = item.transform.position;
        Quaternion originalRotation = item.transform.rotation;
        bool launched = false;
        item.SetSubmissionReturning(true);
        item.Holder?.Interact.TryReleaseHeld(item);
        item.OnDrop();
        item.ChangePickState(false);
        item.SetPhysicsState(false);
        try
        {
            Vector3 intake = suctionPoint != null ? suctionPoint.position : zone.bounds.center;
            yield return MovePlate(item, id, intake, originalRotation, suctionDuration);
            if (!IsSamePlate(item, id)) yield break;
            // S_DishEject can arrive before the world snapshot that removes the food.
            // Wait for server confirmation; never delete its food locally.
            float deadline = Time.unscaledTime + 2f;
            PlateInteraction plate = item.GetComponent<PlateInteraction>();
            while (IsSamePlate(item, id) && !plate.IsEmpty && Time.unscaledTime < deadline) yield return null;
            if (!IsSamePlate(item, id)) yield break;
            if (!plate.IsEmpty)
            {
                Debug.LogWarning($"[Submit Return] 음식 삭제 확인 대기 초과: {id}", item);
                yield break;
            }
            Vector3 exit = returnPoint != null ? returnPoint.position : originalPosition;
            Quaternion rotation = returnPoint != null ? returnPoint.rotation : originalRotation;
            // Ejection is a separate animation, after intake and food removal finish.
            yield return MovePlate(item, id, exit, rotation, ejectDuration);
            if (!IsSamePlate(item, id)) yield break;
            item.SetPhysicsState(true);
            Transform directionBasis = returnPoint != null ? returnPoint : transform;
            if (item.Rb != null && ejectDirection.sqrMagnitude > 0f)
                item.Rb.AddForce(directionBasis.TransformDirection(ejectDirection).normalized * ejectForce, ForceMode.Impulse);
            launched = true;
        }
        finally
        {
            if (IsSamePlate(item, id))
            {
                if (!launched) item.transform.SetPositionAndRotation(originalPosition, originalRotation);
                item.SetPhysicsState(true);
                item.ChangePickState(true);
                item.ReleaseSubmissionLock();
                item.SetSubmissionReturning(false);
            }
        }
    }

    private static bool IsSamePlate(CatchableObj item, long id) =>
        item != null && item.gameObject.activeInHierarchy && item.NetworkId == id;

    private static IEnumerator MovePlate(CatchableObj item, long id, Vector3 destination, Quaternion rotation, float duration)
    {
        Vector3 start = item.transform.position;
        Quaternion startRotation = item.transform.rotation;
        float elapsed = 0f;
        while (IsSamePlate(item, id) && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
            item.transform.SetPositionAndRotation(Vector3.Lerp(start, destination, t), Quaternion.Slerp(startRotation, rotation, t));
            yield return null;
        }
        if (IsSamePlate(item, id)) item.transform.SetPositionAndRotation(destination, rotation);
    }
}
