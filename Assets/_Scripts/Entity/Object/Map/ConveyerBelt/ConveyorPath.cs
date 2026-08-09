using System.Collections.Generic;
using UnityEngine;
public class ConveyorPath : MonoBehaviour
{
    [SerializeField] private Transform[] waypoints; // 시작 - 코너1 - 코너2 - ... - 끝
    [SerializeField] private float cornerRadius = 1f;

    private readonly List<IConveyorSegment> segments = new();
    private float[] cumulativeStart;

    public float TotalLength { get; private set; }
    public Transform[] Waypoints => waypoints;
    public int WaypointCount => waypoints?.Length ?? 0;

    void Awake() => Build();

    /// <summary>에디터 툴에서 waypoint 배열을 갱신할 때 사용.</summary>
    public void SetWaypoints(Transform[] newWaypoints)
    {
        waypoints = newWaypoints;
        Build();
    }

    public void Build()
    {
        segments.Clear();

        if (waypoints == null || waypoints.Length < 2)
        {
            TotalLength = 0f;
            return;
        }

        foreach (var wp in waypoints)
        {
            if (wp == null)
            {
                TotalLength = 0f;
                return; // 비어있는 슬롯이 있으면 빌드 보류
            }
        }

        int interiorCount = waypoints.Length - 2;
        Vector3 entryPoint = waypoints[0].position;

        for (int i = 1; i <= interiorCount; i++)
        {
            Vector3 prevPos = waypoints[i - 1].position;
            Vector3 cornerPos = waypoints[i].position;
            Vector3 nextPos = waypoints[i + 1].position;

            Vector3 inDir = (cornerPos - prevPos).normalized;
            Vector3 outDir = (nextPos - cornerPos).normalized;

            float inSegLen = Vector3.Distance(prevPos, cornerPos);
            float outSegLen = Vector3.Distance(cornerPos, nextPos);
            float safeRadiusIn = Mathf.Min(cornerRadius, inSegLen * 0.5f);
            float safeRadiusOut = Mathf.Min(cornerRadius, outSegLen * 0.5f);

            Vector3 cornerEntry = cornerPos - inDir * safeRadiusIn;
            Vector3 cornerExit = cornerPos + outDir * safeRadiusOut;

            segments.Add(new StraightSegment(entryPoint, cornerEntry));
            segments.Add(new CornerSegment(cornerEntry, cornerPos, cornerExit));

            entryPoint = cornerExit;
        }

        Vector3 endPoint = waypoints[waypoints.Length - 1].position;
        segments.Add(new StraightSegment(entryPoint, endPoint));

        cumulativeStart = new float[segments.Count];
        float acc = 0f;
        for (int i = 0; i < segments.Count; i++)
        {
            cumulativeStart[i] = acc;
            acc += segments[i].Length;
        }
        TotalLength = acc;
    }

    public (Vector3 point, Vector3 tangent) Evaluate(float distance)
    {
        if (segments.Count == 0)
            return (transform.position, transform.forward);

        distance = Mathf.Clamp(distance, 0f, TotalLength);

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (distance >= cumulativeStart[i])
            {
                float local = distance - cumulativeStart[i];
                return (segments[i].GetPoint(local), segments[i].GetTangent(local));
            }
        }
        return (segments[0].GetPoint(0), segments[0].GetTangent(0));
    }
    /// <summary>
    /// 임의의 월드 좌표(예: 벨트 위에 떨어진 재료 위치)에 대해
    /// 경로 상에서 가장 가까운 지점의 distance 값을 찾는다.
    /// 드롭 재등록처럼 "이 위치가 경로상 몇 미터 지점인가"를 역산할 때 사용.
    /// 정밀한 역함수가 아니라 coarse-to-fine 샘플링 기반 근사치이며,
    /// 벨트 이동에서 이미 오차를 허용하기로 한 전제와 일치한다.
    /// </summary>
    public float GetNearestDistance(Vector3 worldPosition, int coarseSamples = 40, int refineSamples = 8)
    {
        if (TotalLength <= 0f) return 0f;

        // 1) 굵은 간격으로 훑어서 대략적인 구간을 찾음
        float bestDistance = 0f;
        float bestSqr = float.MaxValue;

        for (int i = 0; i <= coarseSamples; i++)
        {
            float d = TotalLength * i / coarseSamples;
            Vector3 p = Evaluate(d).point;
            float sqr = (p - worldPosition).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestDistance = d;
            }
        }

        // 2) 찾은 지점 주변을 좁혀서 정밀도를 조금 더 높임
        float coarseStep = TotalLength / coarseSamples;
        float rangeStart = Mathf.Max(0f, bestDistance - coarseStep);
        float rangeEnd = Mathf.Min(TotalLength, bestDistance + coarseStep);

        for (int i = 0; i <= refineSamples; i++)
        {
            float d = Mathf.Lerp(rangeStart, rangeEnd, i / (float)refineSamples);
            Vector3 p = Evaluate(d).point;
            float sqr = (p - worldPosition).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestDistance = d;
            }
        }

        return bestDistance;
    }


#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length < 2) return;

        Build();
        if (TotalLength <= 0f) return;

        Gizmos.color = Color.yellow;
        int steps = 50;
        Vector3 prev = Evaluate(0).point;
        for (int i = 1; i <= steps; i++)
        {
            float d = TotalLength * i / steps;
            Vector3 p = Evaluate(d).point;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.color = i == 0 ? Color.green : (i == waypoints.Length - 1 ? Color.red : Color.yellow);
            Gizmos.DrawSphere(waypoints[i].position, 0.15f);
        }
    }
#endif
}