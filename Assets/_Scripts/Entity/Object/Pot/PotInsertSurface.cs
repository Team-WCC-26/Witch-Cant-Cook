using System;
using System.Collections.Generic;
using UnityEngine;

// 초록색 표면의 실제 삼각형만 조준 대상으로 사용한다. 물리 콜라이더는 추가하지 않는다.
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class PotInsertSurface : MonoBehaviour
{
    [SerializeField, Min(0.1f), InspectorName("재료 투입 허용 거리")]
    private float insertReach = 3f;
    [SerializeField, HideInInspector] private Vector3[] vertices = Array.Empty<Vector3>();
    [SerializeField, HideInInspector] private int[] triangles = Array.Empty<int>();

    private static readonly HashSet<PotInsertSurface> activeSurfaces = new();
    private static RaycastHit[] visibilityHits = new RaycastHit[32];
    private PotInteraction pot;
    private Renderer surfaceRenderer;

    public PotInteraction Pot => pot != null ? pot : pot = GetComponentInParent<PotInteraction>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => activeSurfaces.Clear();

    private void OnEnable() => activeSurfaces.Add(this);
    private void OnDisable() => activeSurfaces.Remove(this);

    public static PotInteraction Find(Camera camera, Transform playerRoot)
    {
        if (camera == null || playerRoot == null) return null;
        Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f));
        PotInteraction nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (PotInsertSurface surface in activeSurfaces)
        {
            if (surface == null || !surface.isActiveAndEnabled || surface.Pot == null ||
                !surface.Pot.isActiveAndEnabled ||
                !surface.TryGetHit(ray, playerRoot.position, out float distance) ||
                distance >= nearestDistance || !IsVisible(ray, distance, playerRoot))
                continue;
            nearest = surface.Pot;
            nearestDistance = distance;
        }
        return nearest;
    }

    public bool TryGetHit(Ray ray, Vector3 playerPosition, out float distance)
    {
        distance = float.PositiveInfinity;
        if (surfaceRenderer == null) surfaceRenderer = GetComponent<Renderer>();
        if (!isActiveAndEnabled || surfaceRenderer == null || !surfaceRenderer.enabled ||
            vertices == null || triangles == null || triangles.Length < 3)
            return false;

        Vector3 origin = transform.InverseTransformPoint(ray.origin);
        // 정규화하지 않아 교점의 거리 계수를 월드 단위로 유지한다.
        Vector3 direction = transform.InverseTransformVector(ray.direction);
        if (direction.y >= -0.000001f) return false;

        for (int index = 0; index + 2 < triangles.Length; index += 3)
        {
            Vector3 a = vertices[triangles[index]];
            Vector3 edge1 = vertices[triangles[index + 1]] - a;
            Vector3 edge2 = vertices[triangles[index + 2]] - a;
            Vector3 cross = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, cross);
            if (Mathf.Abs(determinant) < 0.0000001f) continue;

            float inverse = 1f / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, cross) * inverse;
            if (u < 0f || u > 1f) continue;
            Vector3 q = Vector3.Cross(offset, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f) continue;

            float candidate = Vector3.Dot(edge2, q) * inverse;
            if (candidate >= 0f && candidate < distance) distance = candidate;
        }

        return !float.IsPositiveInfinity(distance) &&
            (ray.GetPoint(distance) - playerPosition).sqrMagnitude <= insertReach * insertReach;
    }

    private static bool IsVisible(Ray ray, float distance, Transform playerRoot)
    {
        int count;
        do
        {
            count = Physics.RaycastNonAlloc(ray, visibilityHits, Mathf.Max(0f, distance - .01f),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count < visibilityHits.Length) break;
            if (visibilityHits.Length >= 1024) return false;
            Array.Resize(ref visibilityHits, visibilityHits.Length * 2);
        } while (true);

        for (int index = 0; index < count; index++)
        {
            Collider collider = visibilityHits[index].collider;
            if (collider == null || collider.transform.IsChildOf(playerRoot)) continue;
            // 솥의 검정 테두리도 가림 대상으로 유지한다.
            return false;
        }
        return true;
    }

#if UNITY_EDITOR
    private void Reset() => BakeSurface();
    private void OnValidate() => BakeSurface();

    [ContextMenu("초록색 표면 판정 갱신")]
    public void BakeSurface()
    {
        if (Application.isPlaying) return;
        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        // 편집 중 원본 메시를 저장해 빌드에서 Read/Write 설정 없이 사용한다.
        vertices = filter.sharedMesh.vertices;
        triangles = filter.sharedMesh.triangles;
    }

    private void OnDrawGizmosSelected()
    {
        if (vertices == null || triangles == null) return;
        Matrix4x4 previous = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.green;
        for (int index = 0; index + 2 < triangles.Length; index += 3)
        {
            Vector3 a = vertices[triangles[index]];
            Vector3 b = vertices[triangles[index + 1]];
            Vector3 c = vertices[triangles[index + 2]];
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, a);
        }
        Gizmos.matrix = previous;
        Gizmos.color = previousColor;
    }
#endif
}
