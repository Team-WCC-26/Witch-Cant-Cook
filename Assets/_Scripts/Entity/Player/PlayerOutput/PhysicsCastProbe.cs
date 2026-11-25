using System;
using UnityEngine;

public sealed class PhysicsCastProbe : MonoBehaviour
{
    public enum CastType { Raycast = 0, SphereCast = 1, BoxCast = 2 }

    [field: SerializeField] public CastType Type { get; set; } = CastType.Raycast;
    [field: SerializeField] public Transform Origin { get; set; }
    [field: SerializeField] public Collider OriginCollider { get; set; }
    [field: SerializeField] public Vector3 StartOffset { get; set; }
    [field: SerializeField] public Vector3 Direction { get; set; } = Vector3.forward;
    [field: SerializeField] public float Distance { get; set; } = 0.5f;
    [field: SerializeField] public float Radius { get; set; } = 0.25f;
    [field: SerializeField] public Vector3 BoxSize { get; set; } = Vector3.one * 0.5f;
    public Quaternion BoxRot => (Origin != null ? Origin : transform).rotation;

    [field: SerializeField] public LayerMask LayerMask { get; set; } = (1 << 7) | (1 << 10);
    [field: SerializeField] public QueryTriggerInteraction TriggerInteraction { get; set; } = QueryTriggerInteraction.Collide;
    [field: SerializeField] public bool DebugCast { get; set; }
    [field: SerializeField] public Color DebugColor { get; set; } = Color.red;

    /// <summary>Builds the world-space cast ray.</summary>
    public Ray BuildRay()
    {
        Transform basis = Origin != null ? Origin : transform;
        Vector3 start = OriginCollider != null ? OriginCollider.bounds.center : basis.position;
        return new Ray(start + basis.TransformDirection(StartOffset), basis.TransformDirection(Direction).normalized);
    }

    /// <summary>Returns hits for the selected cast shape.</summary>
    public RaycastHit[] Cast(out Ray ray)
    {
        ray = BuildRay();
        if (!isActiveAndEnabled || Distance <= 0f || ray.direction.sqrMagnitude < 0.0001f)
            return Array.Empty<RaycastHit>();

        return Type switch
        {
            CastType.Raycast => Physics.RaycastAll(ray, Distance, LayerMask, TriggerInteraction),
            CastType.SphereCast => Physics.SphereCastAll(ray, Mathf.Max(0.01f, Radius), Distance, LayerMask, TriggerInteraction),
            CastType.BoxCast => Physics.BoxCastAll(ray.origin, GetBoxHalfExtents(), ray.direction,
                BoxRot, Distance, LayerMask, TriggerInteraction),
            _ => Array.Empty<RaycastHit>()
        };
    }

    /// <summary>Draws the selected cast volume.</summary>
    public void DrawDebug()
    {
        if (!isActiveAndEnabled || !DebugCast) return;
        Ray ray = BuildRay();
        Vector3 end = ray.GetPoint(Distance);
        Debug.DrawLine(ray.origin, end, DebugColor);
        if (Type == CastType.BoxCast)
        {
            DrawBoxCast(ray, end);
            return;
        }
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

    /// <summary>Returns clamped box half extents in world units.</summary>
    public Vector3 GetBoxHalfExtents()
    {
        return Vector3.Max(BoxSize * .5f, Vector3.one * .01f);
    }

    private void DrawBoxCast(Ray ray, Vector3 end)
    {
        Vector3 halfExtents = GetBoxHalfExtents();
        Quaternion rot = BoxRot;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = rot * new Vector3(
                (corner & 1) == 0 ? -halfExtents.x : halfExtents.x,
                (corner & 2) == 0 ? -halfExtents.y : halfExtents.y,
                (corner & 4) == 0 ? -halfExtents.z : halfExtents.z);
            Debug.DrawLine(ray.origin + offset, end + offset, DebugColor);
            for (int axis = 0; axis < 3; axis++)
            {
                int bit = 1 << axis;
                if ((corner & bit) != 0) continue;
                Vector3 edge = Vector3.zero;
                edge[axis] = halfExtents[axis] * 2f;
                edge = rot * edge;
                Debug.DrawLine(ray.origin + offset, ray.origin + offset + edge, DebugColor);
                Debug.DrawLine(end + offset, end + offset + edge, DebugColor);
            }
        }
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
#if UNITY_EDITOR
#region Editor
[UnityEditor.CustomEditor(typeof(PhysicsCastProbe)), UnityEditor.CanEditMultipleObjects]
public sealed class PhysicsCastProbeEditor : UnityEditor.Editor
{
    /// <summary>Draws common settings and fields for the selected cast type.</summary>
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new UnityEditor.EditorGUI.DisabledScope(true))
            UnityEditor.EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        DrawHeader("Cast");
        UnityEditor.SerializedProperty type = GetField("Type");
        UnityEditor.EditorGUILayout.PropertyField(type);
        DrawField("Distance");
        if (!type.hasMultipleDifferentValues)
        {
            switch ((PhysicsCastProbe.CastType)type.enumValueIndex)
            {
                case PhysicsCastProbe.CastType.SphereCast:
                    DrawHeader("Sphere");
                    DrawField("Radius", "Sphere Radius");
                    break;
                case PhysicsCastProbe.CastType.BoxCast:
                    DrawHeader("Box");
                    DrawField("BoxSize", "Box Size (World Units)");
                    break;
            }
        }

        DrawHeader("Origin / Direction");
        DrawField("Origin");
        DrawField("OriginCollider");
        DrawField("StartOffset");
        DrawField("Direction");

        DrawHeader("Collision Filter");
        DrawField("LayerMask");
        DrawField("TriggerInteraction");

        DrawHeader("Debug");
        DrawField("DebugCast");
        UnityEditor.SerializedProperty debugCast = GetField("DebugCast");
        if (!debugCast.hasMultipleDifferentValues && debugCast.boolValue) DrawField("DebugColor");
        serializedObject.ApplyModifiedProperties();
    }

    private UnityEditor.SerializedProperty GetField(string name)
    {
        return serializedObject.FindProperty($"<{name}>k__BackingField");
    }

    private void DrawField(string name, string label = null)
    {
        UnityEditor.SerializedProperty property = GetField(name);
        if (label == null) UnityEditor.EditorGUILayout.PropertyField(property);
        else UnityEditor.EditorGUILayout.PropertyField(property, new GUIContent(label));
    }

    private static void DrawHeader(string title)
    {
        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField(title, UnityEditor.EditorStyles.boldLabel);
    }
}
#endregion
#endif
