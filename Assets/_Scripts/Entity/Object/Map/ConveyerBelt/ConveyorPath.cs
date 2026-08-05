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