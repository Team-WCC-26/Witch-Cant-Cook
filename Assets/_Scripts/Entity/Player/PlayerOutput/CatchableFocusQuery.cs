using System.Collections.Generic;
using UnityEngine;

// One camera-centered pickup query shared by interaction and its visual focus indicator.
public sealed class CatchableFocusQuery
{
    private Collider[] candidates = new Collider[64];
    private RaycastHit[] visibilityHits = new RaycastHit[32];
    private readonly HashSet<CatchableObj> visited = new();

    public CatchableObj Find(Camera camera, Ray ray, float distance, float radius, Transform playerRoot)
    {
        if (camera == null || distance <= 0f) return null;
        int count;
        do
        {
            count = Physics.OverlapCapsuleNonAlloc(ray.origin, ray.GetPoint(distance), Mathf.Max(.01f, radius),
                candidates, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            if (count < candidates.Length) break;
            if (candidates.Length >= 1024) return null;
            System.Array.Resize(ref candidates, candidates.Length * 2);
        } while (true);

        visited.Clear();
        CatchableObj best = null;
        float bestScore = float.PositiveInfinity;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = candidates[i];
            if (collider == null || (playerRoot != null && collider.transform.IsChildOf(playerRoot))) continue;
            CatchableObj item = collider.GetComponentInParent<CatchableObj>();
            if (item == null || !visited.Add(item) || !IsAvailable(item) || !TryGetBounds(item, out Bounds bounds)) continue;
            if (Vector3.Dot(bounds.center - ray.origin, ray.direction) <= 0f) continue;
            float range = Vector3.Distance(ray.origin, bounds.ClosestPoint(ray.origin));
            if (range > distance) continue;
            Vector3 viewport = camera.WorldToViewportPoint(bounds.center);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) continue;
            Vector2 offset = new((viewport.x - .5f) * camera.aspect, viewport.y - .5f);
            float score = offset.sqrMagnitude;
            if (score > bestScore || (score == bestScore && range >= bestDistance)) continue;
            if (!IsVisible(camera.transform.position, bounds.center, item, playerRoot)) continue;
            best = item;
            bestScore = score;
            bestDistance = range;
        }
        return best;
    }

    public static bool IsAvailable(CatchableObj item) => item != null && item.isActiveAndEnabled &&
        item.gameObject.activeInHierarchy && item.CanBePicked && !item.IsHold;

    public static bool TryGetBounds(CatchableObj item, out Bounds bounds)
    {
        Collider collider = item != null ? item.Col : null;
        if (collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy)
        {
            bounds = collider.bounds;
            return true;
        }
        bounds = default;
        return false;
    }

    private bool IsVisible(Vector3 origin, Vector3 point, CatchableObj item, Transform playerRoot)
    {
        Vector3 delta = point - origin;
        float distance = delta.magnitude;
        if (distance < .001f) return true;
        int count;
        do
        {
            count = Physics.RaycastNonAlloc(origin, delta / distance, visibilityHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count < visibilityHits.Length) break;
            if (visibilityHits.Length >= 1024) return false;
            System.Array.Resize(ref visibilityHits, visibilityHits.Length * 2);
        } while (true);
        for (int i = 0; i < count; i++)
        {
            Collider collider = visibilityHits[i].collider;
            if (collider == null || (playerRoot != null && collider.transform.IsChildOf(playerRoot))) continue;
            if (collider.GetComponentInParent<CatchableObj>() != item) return false;
        }
        return true;
    }
}
