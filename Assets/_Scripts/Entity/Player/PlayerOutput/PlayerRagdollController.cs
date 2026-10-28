using System.Collections.Generic;
using UnityEngine;

public class PlayerRagdollController
{
    private readonly PlayerBrain brain;
    private readonly PlayerAnimController animController;
    private readonly IReadOnlyList<BodyPart> bodyParts;

    private const float MaxEnterAngularVelocity = 3f;
    private const float RemoteFollowSpeed = 10f;
    private const float RemoteMaxCorrectionSpeed = 8f;
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
    private Vector3 pelvisOffset;
    private bool hasRemoteTarget;
    private Vector3 remotePelvisPos;
    private Vector3 remoteVelocity;
    private Vector3 ragdollRootLocalPos;
    private Quaternion ragdollRootLocalRot;
    private readonly float[] dampingBeforeLaunch;

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

    public void Enter()
    {
        if (isRagdoll)
        {
            return;
        }

        isRagdoll = true;
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
        brain.Col.isTrigger = true;

        if (brain.Rb != null)
        {
            brain.Rb.useGravity = false;
            brain.Rb.linearVelocity = Vector3.zero;
            brain.Rb.angularVelocity = Vector3.zero;
        }

        SetRagdollPhysicsActive(true);
        ClampRagdollAngularVelocity();

        Physics.SyncTransforms();
        if (pelvisBody != null) pelvisOffset = pelvisBody.position - brain.transform.position;
    }

    // Apply the same launch speed to every body part.
    public void Launch(Vector3 pos, Vector3 impulse)
    {
        if (isRagdoll) Recover();
        brain.transform.position = pos;
        Physics.SyncTransforms();
        Enter();

        Vector3 velocity = impulse / Mathf.Max(0.01f, brain.Rb.mass);
        for (int index = 0; index < bodyParts.Count; index++)
        {
            BodyPart part = bodyParts[index];
            if (part.Rb == null) continue;
            dampingBeforeLaunch[index] = part.Rb.linearDamping;
            part.Rb.linearDamping = brain.Rb.linearDamping * 0.5f;
            part.Rb.linearVelocity = velocity;
            part.Rb.angularVelocity = Vector3.zero;
        }
        isLaunched = pelvisBody != null;
        Physics.SyncTransforms();
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
        if (!isLaunched || pelvisBody.linearVelocity.y >= 0f) return;

        Vector3 extraGravity = Physics.gravity * (brain.FallMultiplier - 1f);
        foreach (BodyPart part in bodyParts)
        {
            if (part.Rb == null || part.Rb.isKinematic) continue;
            part.Rb.AddForce(extraGravity, ForceMode.Acceleration);
        }
    }

    // Keep the local player root at the simulated pelvis.
    public void SyncRoot()
    {
        if (!isRagdoll || pelvisBody == null) return;
        if (brain.StateResolver is not LocalPlayerStateResolver) return;
        MoveRoot(pelvisBody.position - pelvisOffset, brain.transform.rotation);
    }

    // Move only the reference root; correct the pelvis through physics.
    public void UpdateRemoteTarget(Vector3 pos, Quaternion rot, Vector3 velocity)
    {
        if (!isRagdoll || pelvisBody == null) return;
        MoveRoot(pos, rot);
        remotePelvisPos = pos + pelvisOffset;
        remoteVelocity = velocity;
        hasRemoteTarget = true;
    }

    // Restart recovery whenever the main body loses support or moves too fast.
    public bool TickRecovery(bool paused, float deltaTime)
    {
        if (!isRagdoll || paused || !IsStableForRecovery())
        {
            recoveryStableTime = 0f;
            return false;
        }

        recoveryStableTime += deltaTime;
        return recoveryStableTime >= RecoverySettleDuration + brain.RagdollStunDuration;
    }

    public void Recover()
    {
        if (!isRagdoll)
        {
            return;
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
        recoveryStableTime = 0f;
        isLaunched = false;
        pelvisBody = null;
        hasRemoteTarget = false;

        ResetRagdollVelocity();
        ResetRootVelocity();

        SetRagdollPhysicsActive(false);

        brain.Animator.transform.SetLocalPositionAndRotation(ragdollRootLocalPos, ragdollRootLocalRot);
        brain.Animator.enabled = true;
        brain.Col.isTrigger = false;

        if (brain.Rb != null)
        {
            brain.Rb.useGravity = true;
        }

        Physics.SyncTransforms();
    }

    private void FollowRemotePelvis()
    {
        if (!hasRemoteTarget) return;
        Vector3 correction = Vector3.ClampMagnitude(
            (remotePelvisPos - pelvisBody.position) * RemoteFollowSpeed, RemoteMaxCorrectionSpeed);
        Vector3 velocityChange = remoteVelocity + correction - pelvisBody.linearVelocity;
        foreach (BodyPart part in bodyParts)
        {
            if (part.Rb == null || part.Rb.isKinematic) continue;
            part.Rb.AddForce(velocityChange, ForceMode.VelocityChange);
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

    private void MoveRoot(Vector3 pos, Quaternion rot)
    {
        // Move the player root without teleporting simulated bones.
        Transform ragdollRoot = brain.Animator.transform;
        Vector3 bodyPos = ragdollRoot.position;
        Quaternion bodyRot = ragdollRoot.rotation;
        brain.transform.SetPositionAndRotation(pos, rot);
        ragdollRoot.SetPositionAndRotation(bodyPos, bodyRot);
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
            if (part.Rb == null)
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
            if (part.Rb == null)
            {
                continue;
            }

            part.Rb.linearVelocity = Vector3.zero;
            part.Rb.angularVelocity = Vector3.zero;
        }
    }

    private void ResetRootVelocity()
    {
        if (brain.Rb == null)
        {
            return;
        }

        brain.Rb.linearVelocity = Vector3.zero;
        brain.Rb.angularVelocity = Vector3.zero;
    }
}