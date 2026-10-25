using System;
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
    private static void ResetQueue() { pending.Clear(); sending = false; }
    private void Awake() => zone = GetComponent<BoxCollider>();
    private void OnEnable()
    {
        if (zone == null) zone = GetComponent<BoxCollider>();
        if (surface != SubmissionSurface.Auto)
            zone.isTrigger = surface == SubmissionSurface.Pedestal;
        lastStatus = $"대기: {ActiveSurface}, 정면={frontFace}, IsTrigger={zone.isTrigger}";
        if (logDiagnostics) Debug.Log($"[Submit Ready] {name}: {lastStatus}", this);
    }
    private void OnDisable()
    {
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
    private void RemoveSample(CatchableObj item)
    {
        if (!samples.TryGetValue(item, out RestSample sample)) return;
        if (item != null) item.OnPicked -= sample.Reset;
        samples.Remove(item);
    }
    private void FixedUpdate()
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
            if (!CheckEligibility(item) || item.Rb == null) { sample.Reset(); continue; }
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
            if (now - sample.Since >= stationarySeconds) Enqueue(item, sample.Since + stationarySeconds);
        }
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

    private static string SubmissionBlock(CatchableObj item)
    {
        if (item == null) return "접시 콜라이더가 아닙니다.";
        if (!item.gameObject.activeInHierarchy) return "비활성 접시";
        if (item.IsSubmissionPending) return "이미 제출 요청됨";
        if (item.IsHold || item.Holder != null) return "손에 든 접시";
        if (!item.IsLocalOwner) return "이 클라이언트의 소유가 아닌 접시";
        if (item.NetworkId <= 0) return "서버 엔티티 ID 없음";
        if (item.ParentEntityId > 0) return $"다른 도구에 들어 있는 접시: Parent={item.ParentEntityId}";
        if (!item.TryGetComponent(out PlateInteraction plate) || plate.IsEmpty) return "빈 접시 또는 음식 정보 없음";
        RoomHudState state = RoomHudSession.Instance.State;
        if (!state.IsStageRunning) return "스테이지 시작 알림 미수신 또는 이미 종료됨";
        return state.RemainingSeconds(Time.unscaledTimeAsDouble) <= 0 ? "스테이지 타이머 0초" : null;
    }
    private bool CheckEligibility(CatchableObj item)
    {
        string reason = SubmissionBlock(item);
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
    private static void Enqueue(CatchableObj item, double qualifiedAt)
    {
        if (ServerManager.Instance == null || SubmissionBlock(item) != null || !item.TryBeginSubmission()) return;
        pending.Add(new SubmissionRequest { Item = item, EntityId = item.NetworkId, QualifiedAt = qualifiedAt });
        Debug.Log($"[Submit Queued] EntityId={item.NetworkId}, conditionTime={qualifiedAt:F3}", item);
    }
    private void LateUpdate()
    {
        if (!sending && pending.Count > 0) SendPendingAsync().Forget();
    }
    private static async UniTask SendPendingAsync()
    {
        sending = true;
        try
        {
            while (pending.Count > 0)
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
                    // Existing server destroys the plate; never award or remove food locally.
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
}
