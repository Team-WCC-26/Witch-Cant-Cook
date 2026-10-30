using System.Collections.Generic;
using UnityEngine;

public class PlayerRagdollController
{
    private readonly PlayerBrain brain;
    private readonly PlayerAnimController animController;
    private readonly IReadOnlyList<BodyPart> bodyParts;

    private const float MaxEnterAngularVelocity = 3f;
    private const float LaunchCollapseDuration = 0.16f;
    private const float LaunchAngularSpeed = 6f;
    private const float RemoteFollowSpeed = 10f;
    private const float RemoteMaxCorrectionSpeed = 8f;
    private const float RemoteRotationFollowSpeed = 10f;
    private const float RemoteMaxAngularSpeed = 8f;
    private const float RemoteMaxAngularAcceleration = 30f;
    private const float RecoveryGroundDistance = 0.12f;
    private const float RecoveryMaxSpeed = 0.5f;
    private const float RecoveryMaxAngularSpeed = 2f;
    private const float RecoverySettleDuration = 0.15f;

    private readonly List<BodyPart> recoveryParts = new();
    private readonly int recoveryGroundMask = ~LayerMask.GetMask("Ragdoll");
    private float recoveryStableTime;

    private bool isRagdoll;
    private bool isLaunched;
    private Rigidbody pelvisBody;
    private bool hasRemoteTarget;
    private Vector3 remotePelvisPos;
    private Vector3 remoteVelocity;
    private Vector3 ragdollRootLocalPos;
    private Quaternion ragdollRootLocalRot;
    private readonly float[] dampingBeforeLaunch;
    private bool rootWasKinematic;
    private bool rootUsedGravity;
    private bool rootColliderEnabled;
    private Quaternion remoteRootRot;
    private Quaternion remotePelvisRot;
    private float rootColliderBottomOffset;
    private bool isLaunchPending;
    private float launchCollapseTime;
    private Vector3 pendingLaunchPos;
    private Vector3 pendingLaunchImpulse;

    public bool IsActive => isRagdoll;
    public bool IsLaunchPending => isLaunchPending;
    public Quaternion PhysicsRot => brain.Body.PhysicsRot;
    public Vector3 PhysicsPos => brain.Body.PhysicsPos;
    public Vector3 VisualPos => brain.Body.VisualPos;

    // Initialize body physics and recovery references.
    public PlayerRagdollController(PlayerBrain brain, PlayerAnimController animController)
    {
        this.brain = brain;
        this.animController = animController;
        bodyParts = brain.BodyParts;
        dampingBeforeLaunch = new float[bodyParts.Count];
        foreach (BodyPart part in bodyParts)
        {
            if (part != null && part.Rb != null &&
                (part.Type == BodyPartType.Pelvis || part.Type == BodyPartType.Spine))
                recoveryParts.Add(part);
        }

        SetRagdollPhysicsActive(false);
    }

    // Enable body physics and suspend root movement.
    public void Enter()
    {
        if (isRagdoll)
        {
            return;
        }

        isRagdoll = true;
        brain.Health?.SetRagdollActive(true);
        recoveryStableTime = 0f;
        ragdollRootLocalPos = brain.Animator.transform.localPosition;
        ragdollRootLocalRot = brain.Animator.transform.localRotation;
        pelvisBody = null;
        foreach (BodyPart part in bodyParts)
        {
            if (part != null && part.Type == BodyPartType.Pelvis)
            {
                pelvisBody = part.Rb;
                break;
            }
        }

        animController.ForceIdle();

        brain.Animator.enabled = false;
        rootColliderEnabled = brain.Col.enabled;
        rootColliderBottomOffset = brain.Col.bounds.min.y - brain.transform.position.y;
        brain.Col.enabled = false;
        remoteRootRot = brain.transform.rotation;

        if (brain.Rb != null)
        {
            rootWasKinematic = brain.Rb.isKinematic;
            rootUsedGravity = brain.Rb.useGravity;
            brain.Rb.useGravity = false;
            if (!brain.Rb.isKinematic)
            {
                brain.Rb.linearVelocity = Vector3.zero;
                brain.Rb.angularVelocity = Vector3.zero;
            }
            brain.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            brain.Rb.isKinematic = true;
        }

        SetRagdollPhysicsActive(true);
        ClampRagdollAngularVelocity();

        Physics.SyncTransforms();
        brain.Body.UseRagdoll(pelvisBody);
    }

    // Let the body collapse before applying the launch impulse.
    public void Launch(Vector3 pos, Vector3 impulse)
    {
        if (isLaunchPending || isLaunched) return;
        Enter();
        pendingLaunchPos = pos;
        pendingLaunchImpulse = impulse;
        launchCollapseTime = 0f;
        isLaunchPending = pelvisBody != null;
    }

    // Apply remote tracking or the local launch gravity.
    public void FixedTick()
    {
        if (!isRagdoll || pelvisBody == null) return;
        if (brain.StateResolver is RemotePlayerStateResolver)
        {
            FollowRemotePelvis();
            return;
        }
        if (isLaunchPending)
        {
            launchCollapseTime += Time.fixedDeltaTime;
            if (launchCollapseTime < LaunchCollapseDuration) return;
            ApplyLaunchImpulse();
            return;
        }
        if (!isLaunched || pelvisBody.linearVelocity.y >= 0f) return;

        Vector3 extraGravity = Physics.gravity * (brain.FallMultiplier - 1f);
        foreach (BodyPart part in bodyParts)
        {
            if (part.Rb == null || part.Rb.isKinematic) continue;
            part.Rb.AddForce(extraGravity, ForceMode.Acceleration);
        }
    }

    // Preserve the root-space snapshot contract without moving the hierarchy.
    public void UpdateRemoteTarget(Vector3 pos, Quaternion rot, Vector3 velocity)
    {
        if (!isRagdoll || pelvisBody == null) return;
        remotePelvisRot = rot;
        remotePelvisPos = pos + brain.Body.RagdollRootOffset;
        remoteVelocity = velocity;
        hasRemoteTarget = true;
    }

    // Restart recovery whenever the main body loses support or moves too fast.
    public bool TickRecovery(bool paused, float deltaTime)
    {
        if (!isRagdoll || isLaunchPending || paused || !IsStableForRecovery())
        {
            recoveryStableTime = 0f;
            return false;
        }

        recoveryStableTime += deltaTime;
        return recoveryStableTime >= RecoverySettleDuration + brain.RagdollStunDuration;
    }

    // Restore root physics at the body's recovery position.
    public void Recover()
    {
        if (!isRagdoll)
        {
            return;
        }
        Vector3 recoveryPos = PhysicsPos;
        // Keep the standing collider above the supporting floor.
        if (pelvisBody != null && Physics.Raycast(pelvisBody.position + Vector3.up * RecoveryGroundDistance,
            Vector3.down, out RaycastHit groundHit, 3f, brain.GroundLayerMask.value & recoveryGroundMask,
            QueryTriggerInteraction.Ignore) && groundHit.normal.y >= 0.5f)
        {
            recoveryPos.y = groundHit.point.y - rootColliderBottomOffset + 0.02f;
        }
        if (isLaunched)
        {
            for (int index = 0; index < bodyParts.Count; index++)
            {
                if (bodyParts[index].Rb != null)
                    bodyParts[index].Rb.linearDamping = dampingBeforeLaunch[index];
            }
        }

        isRagdoll = false;
        brain.Health?.SetRagdollActive(false);
        recoveryStableTime = 0f;
        isLaunched = false;
        isLaunchPending = false;
        pelvisBody = null;
        hasRemoteTarget = false;

        ResetRagdollVelocity();
        ResetRootVelocity();

        SetRagdollPhysicsActive(false);

        brain.Rb.position = recoveryPos;
        if (brain.StateResolver is RemotePlayerStateResolver) brain.Rb.rotation = remoteRootRot;
        brain.Animator.transform.SetLocalPositionAndRotation(ragdollRootLocalPos, ragdollRootLocalRot);
        brain.Animator.enabled = true;
        brain.Col.enabled = rootColliderEnabled;
        brain.Body.UseRoot();

        if (brain.Rb != null)
        {
            brain.Rb.isKinematic = rootWasKinematic;
            brain.Rb.collisionDetectionMode = rootWasKinematic
                ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.ContinuousDynamic;
            brain.Rb.useGravity = rootUsedGravity;
        }

        Physics.SyncTransforms();
    }

    private void ApplyLaunchImpulse()
    {
        Vector3 offset = pendingLaunchPos - PhysicsPos;
        Vector3 velocity = pendingLaunchImpulse / Mathf.Max(0.01f, brain.Rb.mass);
        Vector3 rotationCenter = Vector3.zero;
        float totalMass = 0f;
        foreach (BodyPart part in bodyParts)
        {
            if (part == null || part.Rb == null) continue;
            rotationCenter += part.Rb.worldCenterOfMass * part.Rb.mass;
            totalMass += part.Rb.mass;
        }
        rotationCenter = totalMass > 0f ? rotationCenter / totalMass : pelvisBody.worldCenterOfMass;
        Vector3 angularVelocity = brain.transform.right * LaunchAngularSpeed;
        for (int index = 0; index < bodyParts.Count; index++)
        {
            BodyPart part = bodyParts[index];
            if (part == null || part.Rb == null) continue;
            dampingBeforeLaunch[index] = part.Rb.linearDamping;
            part.Rb.linearDamping = brain.Rb.linearDamping * 0.5f;
            // Match orbital and angular motion around the shared center of mass.
            Vector3 rotationVelocity = Vector3.Cross(angularVelocity, part.Rb.worldCenterOfMass - rotationCenter);
            // One-time ejection placement; retain the collapsed pose and angular velocity.
            part.Rb.position += offset;
            part.Rb.AddForce(velocity + rotationVelocity, ForceMode.VelocityChange);
            part.Rb.AddTorque(angularVelocity, ForceMode.VelocityChange);
        }
        isLaunchPending = false;
        isLaunched = true;
    }

    private void FollowRemotePelvis()
    {
        if (!hasRemoteTarget) return;
        Vector3 correction = Vector3.ClampMagnitude(
            (remotePelvisPos - pelvisBody.position) * RemoteFollowSpeed, RemoteMaxCorrectionSpeed);
        Vector3 velocityChange = remoteVelocity + correction - pelvisBody.linearVelocity;
        Quaternion rotationError = remotePelvisRot * Quaternion.Inverse(pelvisBody.rotation);
        if (rotationError.w < 0f)
            rotationError = new Quaternion(-rotationError.x, -rotationError.y, -rotationError.z, -rotationError.w);
        rotationError.ToAngleAxis(out float angle, out Vector3 axis);
        Vector3 targetAngularVelocity = angle > 0.01f
            ? axis * Mathf.Min(angle * Mathf.Deg2Rad * RemoteRotationFollowSpeed, RemoteMaxAngularSpeed)
            : Vector3.zero;
        Vector3 angularVelocityChange = Vector3.ClampMagnitude(
            targetAngularVelocity - pelvisBody.angularVelocity, RemoteMaxAngularAcceleration * Time.fixedDeltaTime);
        foreach (BodyPart part in bodyParts)
        {
            if (part.Rb == null || part.Rb.isKinematic) continue;
            // Rotate the connected body together while keeping pelvis tracking independent.
            Vector3 rotationVelocityChange = Vector3.Cross(angularVelocityChange,
                part.Rb.worldCenterOfMass - pelvisBody.worldCenterOfMass);
            part.Rb.AddForce(velocityChange + rotationVelocityChange, ForceMode.VelocityChange);
            part.Rb.AddTorque(angularVelocityChange, ForceMode.VelocityChange);
        }
    }

    private bool IsStableForRecovery()
    {
        bool hasSupport = false;
        RaycastHit supportHit = default;
        foreach (BodyPart part in recoveryParts)
        {
            if (part.Col == null || !part.Col.enabled) continue;
            Bounds bounds = part.Col.bounds;
            if (!Physics.Raycast(bounds.center, Vector3.down, out RaycastHit hit,
                bounds.extents.y + RecoveryGroundDistance, recoveryGroundMask,
                QueryTriggerInteraction.Ignore) || hit.normal.y < 0.5f) continue;

            supportHit = hit;
            hasSupport = true;
            break;
        }
        if (!hasSupport) return false;

        Rigidbody supportBody = supportHit.rigidbody;
        foreach (BodyPart part in recoveryParts)
        {
            Vector3 groundVelocity = supportBody != null
                ? supportBody.GetPointVelocity(part.Rb.worldCenterOfMass) : Vector3.zero;
            Vector3 groundAngularVelocity = supportBody != null
                ? supportBody.angularVelocity : Vector3.zero;
            if ((part.Rb.linearVelocity - groundVelocity).sqrMagnitude > RecoveryMaxSpeed * RecoveryMaxSpeed ||
                (part.Rb.angularVelocity - groundAngularVelocity).sqrMagnitude >
                RecoveryMaxAngularSpeed * RecoveryMaxAngularSpeed)
                return false;
        }
        return true;
    }

    private void SetRagdollPhysicsActive(bool active)
    {
        foreach (var part in bodyParts)
        {
            if (part.Rb == null)
            {
                continue;
            }

            if (!active) part.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            part.Rb.isKinematic = !active;
            if (active) part.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            part.Rb.useGravity = active;
            part.Rb.detectCollisions = true;
        }
    }

    private void ClampRagdollAngularVelocity()
    {
        foreach (var part in bodyParts)
        {
            if (part.Rb == null || part.Rb.isKinematic)
            {
                continue;
            }

            part.Rb.angularVelocity = Vector3.ClampMagnitude(
                part.Rb.angularVelocity,
                MaxEnterAngularVelocity
            );
        }
    }

    private void ResetRagdollVelocity()
    {
        foreach (var part in bodyParts)
        {
            if (part.Rb == null || part.Rb.isKinematic)
            {
                continue;
            }

            part.Rb.linearVelocity = Vector3.zero;
            part.Rb.angularVelocity = Vector3.zero;
        }
    }

    private void ResetRootVelocity()
    {
        if (brain.Rb == null || brain.Rb.isKinematic)
        {
            return;
        }

        brain.Rb.linearVelocity = Vector3.zero;
        brain.Rb.angularVelocity = Vector3.zero;
    }
}
