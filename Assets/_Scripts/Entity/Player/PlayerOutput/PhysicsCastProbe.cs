using System;
using UnityEngine;

public sealed class PhysicsCastProbe : MonoBehaviour
{
    public enum CastType { Raycast, SphereCast }

    [field: SerializeField] public CastType Type { get; set; } = CastType.Raycast;
    [field: SerializeField] public Transform Origin { get; set; }
    [field: SerializeField] public Collider OriginCollider { get; set; }
    [field: SerializeField] public Vector3 StartOffset { get; set; }
    [field: SerializeField] public Vector3 Direction { get; set; } = Vector3.forward;
    [field: SerializeField, Min(0.01f)] public float Distance { get; set; } = 0.5f;
    [field: SerializeField, Min(0.01f)] public float Radius { get; set; } = 0.25f;
    [field: SerializeField] public LayerMask LayerMask { get; set; } = (1 << 7) | (1 << 10);
    [field: SerializeField] public QueryTriggerInteraction TriggerInteraction { get; set; } = QueryTriggerInteraction.Collide;
    [field: SerializeField] public bool DebugCast { get; set; }
    [field: SerializeField] public Color DebugColor { get; set; } = Color.red;

    public Ray BuildRay()
    {
        Transform basis = Origin != null ? Origin : transform;
        Vector3 start = OriginCollider != null ? OriginCollider.bounds.center : basis.position;
        return new Ray(start + basis.TransformDirection(StartOffset), basis.TransformDirection(Direction).normalized);
    }

    public RaycastHit[] Cast(out Ray ray)
    {
        ray = BuildRay();
        if (!isActiveAndEnabled || Distance <= 0f || ray.direction.sqrMagnitude < 0.0001f)
            return Array.Empty<RaycastHit>();

        return Type == CastType.Raycast
            ? Physics.RaycastAll(ray, Distance, LayerMask, TriggerInteraction)
            : Physics.SphereCastAll(ray, Mathf.Max(0.01f, Radius), Distance, LayerMask, TriggerInteraction);
    }

    public void DrawDebug()
    {
        if (!isActiveAndEnabled || !DebugCast) return;
        Ray ray = BuildRay();
        Vector3 end = ray.GetPoint(Distance);
        Debug.DrawLine(ray.origin, end, DebugColor);
        if (Type != CastType.SphereCast) return;

        float radius = Mathf.Max(0.01f, Radius);
        BuildCircleBasis(ray.direction, out Vector3 right, out Vector3 up);
        DrawCircle(ray.origin, right, up, radius);
        DrawCircle(end, right, up, radius);
        Debug.DrawLine(ray.origin + right * radius, end + right * radius, DebugColor);
        Debug.DrawLine(ray.origin - right * radius, end - right * radius, DebugColor);
        Debug.DrawLine(ray.origin + up * radius, end + up * radius, DebugColor);
        Debug.DrawLine(ray.origin - up * radius, end - up * radius, DebugColor);
    }

    private void DrawCircle(Vector3 center, Vector3 right, Vector3 up, float radius)
    {
        const int SegmentCount = 24;
        Vector3 previous = center + right * radius;
        for (int segment = 1; segment <= SegmentCount; segment++)
        {
            float angle = segment * Mathf.PI * 2f / SegmentCount;
            Vector3 next = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            Debug.DrawLine(previous, next, DebugColor);
            previous = next;
        }
    }

    private static void BuildCircleBasis(Vector3 direction, out Vector3 right, out Vector3 up)
    {
        right = Vector3.Cross(direction, Vector3.up);
        if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(direction, Vector3.forward);
        right.Normalize();
        up = Vector3.Cross(right, direction).normalized;
    }
}