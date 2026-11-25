using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerBodyContext
{
    private readonly PlayerBrain brain;
    private readonly Collider[] rootColliders;
    private readonly List<Collider> ragdollColliders = new();
    private Rigidbody ragdollBody;
    private Collider ragdollCollider;

    public bool IsRagdoll => ragdollBody != null;
    public Rigidbody Rigidbody => IsRagdoll ? ragdollBody : brain.Rb;
    public Collider PrimaryCollider => IsRagdoll ? ragdollCollider : brain.Col;
    public IReadOnlyList<Collider> Colliders => IsRagdoll ? ragdollColliders : rootColliders;
    public Vector3 RagdollRootOffset { get; private set; }
    public Vector3 PhysicsPos => IsRagdoll ? ragdollBody.position - RagdollRootOffset
        : brain.Rb != null ? brain.Rb.position : brain.transform.position;
    public Vector3 VisualPos => IsRagdoll ? ragdollBody.transform.position - RagdollRootOffset
        : brain.transform.position;
    public Quaternion PhysicsRot => IsRagdoll ? ragdollBody.rotation : brain.transform.rotation;
    public Vector3 LinearVelocity => Rigidbody != null ? Rigidbody.linearVelocity : Vector3.zero;
    public Vector3 AngularVelocity => Rigidbody != null ? Rigidbody.angularVelocity : Vector3.zero;
    public Bounds Bounds
    {
        get
        {
            Bounds bounds = new(PhysicsPos, Vector3.zero);
            bool hasBounds = false;
            foreach (Collider collider in Colliders)
            {
                if (!IsActive(collider)) continue;
                if (hasBounds) bounds.Encapsulate(collider.bounds);
                else bounds = collider.bounds;
                hasBounds = true;
            }
            return bounds;
        }
    }

    public PlayerBodyContext(PlayerBrain brain)
    {
        this.brain = brain;
        rootColliders = new[] { brain.Col };
        foreach (BodyPart part in brain.BodyParts)
        {
            if (part != null && part.Col != null && !ragdollColliders.Contains(part.Col))
                ragdollColliders.Add(part.Col);
        }
    }

    public void UseRagdoll(Rigidbody body)
    {
        ragdollBody = body;
        ragdollCollider = body != null ? body.GetComponent<Collider>() : null;
        RagdollRootOffset = body != null ? body.position - brain.transform.position : Vector3.zero;
    }

    public void UseRoot()
    {
        ragdollBody = null;
        ragdollCollider = null;
        RagdollRootOffset = Vector3.zero;
    }

    public bool Overlaps(Collider volume)
    {
        if (!IsActive(volume)) return false;
        Pose volumePose = GetPhysicsPose(volume);
        foreach (Collider collider in Colliders)
        {
            if (!IsActive(collider)) continue;
            Pose colliderPose = GetPhysicsPose(collider);
            if (Physics.ComputePenetration(volume, volumePose.position, volumePose.rotation,
                collider, colliderPose.position, colliderPose.rotation, out _, out _)) return true;
        }
        return false;
    }

    public bool IsClearOf(Bounds area)
    {
        foreach (Collider collider in Colliders)
        {
            if (IsActive(collider) && area.Intersects(collider.bounds)) return false;
        }
        return true;
    }

    public Vector3 GetWorldPoint(Transform anchor, Vector3 localOffset)
    {
        Transform reference = anchor != null ? anchor : brain.transform;
        Vector3 point = reference.TransformPoint(localOffset);
        if (!IsRagdoll || !reference.IsChildOf(brain.transform)) return point;
        // Bone anchors already follow the ragdoll's physics.
        if (reference.GetComponentInParent<BodyPart>() != null) return point;
        return point + VisualPos - brain.transform.position;
    }

    private static Pose GetPhysicsPose(Collider collider)
    {
        Rigidbody body = collider.attachedRigidbody;
        if (body == null) return new Pose(collider.transform.position, collider.transform.rotation);
        // Collider transforms may still display an interpolated physics pose.
        Quaternion delta = body.rotation * Quaternion.Inverse(body.transform.rotation);
        return new Pose(body.position + delta * (collider.transform.position - body.transform.position),
            delta * collider.transform.rotation);
    }
    private static bool IsActive(Collider collider)
    {
        return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
    }
}
