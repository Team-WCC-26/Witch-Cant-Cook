using UnityEngine;
using UnityEngine.Rendering;

public sealed class PlayerThrowTracker
{
    private readonly PlayerBrain brain;
    private readonly Vector3[] points = new Vector3[4097];
    private readonly RaycastHit[] hits = new RaycastHit[128];
    private LineRenderer line;
    private Material fallbackMaterial;
    private Rigidbody meshProbeBody;
    private MeshCollider meshProbeCollider;

    public PlayerThrowTracker(PlayerBrain brain)
    {
        this.brain = brain;
    }

    public void Show(CatchableObj target, Vector3 startPos, Vector3 velocity)
    {
        EnsureLine();
        if (line == null) return;
        line.sharedMaterial = brain.ThrowTrajectoryMaterial != null ? brain.ThrowTrajectoryMaterial : fallbackMaterial;
        float step = Time.fixedDeltaTime;
        float damping = target.Rb != null ? target.Rb.linearDamping : 0f;
        float remainingDistance = Mathf.Max(0.1f, brain.ThrowMaxDistance);
        Collider source = target.Col;
        if (source == null || source.isTrigger)
        {
            foreach (Collider candidate in target.GetComponentsInChildren<Collider>())
            {
                if (!candidate.enabled || candidate.isTrigger || candidate.attachedRigidbody != target.Rb) continue;
                source = candidate;
                break;
            }
        }
        Vector3 currentPos = startPos;
        points[0] = currentPos;
        int count = 1;
        for (int index = 1; index < points.Length; index++)
        {
            // Match the thrown Rigidbody: gravity, then PhysX linear damping, then movement.
            velocity = (velocity + Physics.gravity * step) * Mathf.Max(0f, 1f - damping * step);
            Vector3 movement = velocity * step;
            float distance = Mathf.Min(movement.magnitude, remainingDistance);
            if (distance <= 0.00001f) break;
            Vector3 direction = movement.normalized;
            bool collided = TryGetCollision(target, source, currentPos, direction, distance, out float hitDistance);
            currentPos += direction * (collided ? hitDistance : distance);
            points[count++] = currentPos;
            remainingDistance -= distance;
            if (collided || remainingDistance <= 0f) break;
        }
        line.positionCount = count;
        for (int index = 0; index < count; index++) line.SetPosition(index, points[index]);
        line.enabled = count > 1;
    }

    public void Hide()
    {
        if (line != null) line.enabled = false;
    }

    public void Dispose()
    {
        if (line != null) Object.Destroy(line.gameObject);
        if (fallbackMaterial != null) Object.Destroy(fallbackMaterial);
        if (meshProbeBody != null) Object.Destroy(meshProbeBody.gameObject);
    }

    private void EnsureLine()
    {
        if (line != null) return;
        Shader shader = Resources.Load<Shader>("ThrowTrajectory");
        if (shader == null && brain.ThrowTrajectoryMaterial == null) return;
        if (shader != null) fallbackMaterial = new Material(shader);
        GameObject lineObj = new("ThrowTrajectory");
        lineObj.transform.SetParent(brain.transform, false);
        line = lineObj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.045f;
        line.textureMode = LineTextureMode.Tile;
        line.textureScale = new Vector2(4f, 1f);
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
    }

    private bool TryGetCollision(CatchableObj target, Collider source, Vector3 currentPos, Vector3 direction,
        float distance, out float hitDistance)
    {
        int mask = 0;
        int sourceLayer = source != null ? source.gameObject.layer : target.gameObject.layer;
        for (int layer = 0; layer < 32; layer++)
            if (!Physics.GetIgnoreLayerCollision(sourceLayer, layer)) mask |= 1 << layer;

        Vector3 offset = currentPos - target.transform.position;
        RaycastHit[] castHits = hits;
        int hitCount;
        if (source is BoxCollider box)
        {
            Vector3 halfSize = Vector3.Scale(box.size * 0.5f, Abs(box.transform.lossyScale));
            hitCount = Physics.BoxCastNonAlloc(box.transform.TransformPoint(box.center) + offset,
                halfSize, direction, hits, box.transform.rotation, distance, mask, QueryTriggerInteraction.Ignore);
        }
        else if (source is SphereCollider sphere)
        {
            Vector3 scale = Abs(sphere.transform.lossyScale);
            float radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            hitCount = Physics.SphereCastNonAlloc(sphere.transform.TransformPoint(sphere.center) + offset,
                radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
        }
        else if (source is CapsuleCollider capsule)
        {
            Vector3 scale = Abs(capsule.transform.lossyScale);
            int axis = capsule.direction;
            float radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
            Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
            Vector3 extent = capsule.transform.TransformDirection(localAxis) *
                Mathf.Max(0f, capsule.height * scale[axis] * 0.5f - radius);
            Vector3 center = capsule.transform.TransformPoint(capsule.center) + offset;
            hitCount = Physics.CapsuleCastNonAlloc(center - extent, center + extent, radius,
                direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
        }
        else if (source is MeshCollider mesh && mesh.convex)
        {
            if (meshProbeBody == null)
            {
                GameObject probe = new("ThrowTrajectoryMeshProbe");
                meshProbeBody = probe.AddComponent<Rigidbody>();
                meshProbeBody.isKinematic = true;
                meshProbeBody.useGravity = false;
                meshProbeCollider = probe.AddComponent<MeshCollider>();
                meshProbeCollider.enabled = false;
                meshProbeCollider.convex = true;
            }
            meshProbeBody.gameObject.layer = sourceLayer;
            meshProbeCollider.sharedMesh = mesh.sharedMesh;
            meshProbeBody.transform.localScale = mesh.transform.lossyScale;
            meshProbeBody.position = mesh.transform.position + offset;
            meshProbeBody.rotation = mesh.transform.rotation;
            meshProbeCollider.enabled = true;
            try
            {
                castHits = meshProbeBody.SweepTestAll(direction, distance, QueryTriggerInteraction.Ignore);
                hitCount = castHits.Length;
            }
            finally
            {
                // The probe participates only in this query, never in simulation.
                meshProbeCollider.enabled = false;
            }
        }
        else
        {
            hitCount = Physics.RaycastNonAlloc(currentPos, direction, hits, distance, mask,
                QueryTriggerInteraction.Ignore);
        }

        hitDistance = float.PositiveInfinity;
        for (int index = 0; index < hitCount; index++)
        {
            Collider collider = castHits[index].collider;
            if (collider == source || (target.Rb != null && collider.attachedRigidbody == target.Rb) ||
                collider.transform.IsChildOf(target.transform)) continue;
            if (source != null && Physics.GetIgnoreCollision(source, collider)) continue;
            if (castHits[index].distance < hitDistance) hitDistance = castHits[index].distance;
        }
        return !float.IsPositiveInfinity(hitDistance);
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
