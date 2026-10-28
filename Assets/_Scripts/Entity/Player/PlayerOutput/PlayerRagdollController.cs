using System.Collections.Generic;
using UnityEngine;

public class PlayerRagdollController
{
    private readonly PlayerBrain brain;
    private readonly PlayerAnimController animController;
    private readonly IReadOnlyList<BodyPart> bodyParts;

    private const float MaxEnterAngularVelocity = 3f;
    private const float LaunchAngularSpeed = 1.5f;
    private const float LaunchLimbAngularSpeed = 0.6f;
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
    private bool rootWasKinematic;
    private bool rootUsedGravity;
    private bool rootColliderEnabled;
    private Quaternion remoteRootRot;
    private float rootColliderBottomOffset;

    public bool IsActive => isRagdoll;
    public Vector3 PhysicsPos => isRagdoll && pelvisBody != null
        ? pelvisBody.position - pelvisOffset : brain.Rb.position;
    public Vector3 VisualPos => isRagdoll && pelvisBody != null
        ? pelvisBody.transform.position - pelvisOffset : brain.transform.position;

    // Temporary diagnostics; remove after the launch regression is identified.
    private System.Text.StringBuilder traceBuffer;
    private float traceUntil;
    private float traceFlushTime;
    private int traceCount;

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
        if (pelvisBody != null) pelvisOffset = pelvisBody.position - brain.transform.position;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        FlushTrace();
        traceBuffer = new System.Text.StringBuilder(32768);
        traceUntil = Time.realtimeSinceStartup + 5f;
        traceFlushTime = Time.realtimeSinceStartup + 0.25f;
        traceCount = 0;
#endif
        Trace("Enter");
    }

    // Apply the same launch speed to every body part.
    public void Launch(Vector3 pos, Vector3 impulse)
    {
        if (isRagdoll) Recover();
        brain.transform.position = pos;
        Physics.SyncTransforms();
        Enter();

        Vector3 velocity = impulse / Mathf.Max(0.01f, brain.Rb.mass);
        Vector3 launchDirection = Vector3.ProjectOnPlane(impulse, Vector3.up).normalized;
        if (launchDirection.sqrMagnitude < 0.0001f)
            launchDirection = brain.transform.forward;
        Vector3 tumbleAxis = Vector3.Cross(Vector3.up, launchDirection).normalized;
        for (int index = 0; index < bodyParts.Count; index++)
        {
            BodyPart part = bodyParts[index];
            if (part.Rb == null) continue;
            dampingBeforeLaunch[index] = part.Rb.linearDamping;
            part.Rb.linearDamping = brain.Rb.linearDamping * 0.5f;
            part.Rb.linearVelocity = Vector3.zero;
            part.Rb.AddForce(velocity, ForceMode.VelocityChange);
            part.Rb.angularVelocity = Vector3.zero;
            if (impulse.sqrMagnitude > 0.0001f)
                part.Rb.AddTorque(GetLaunchAngularVelocity(part.Type, tumbleAxis), ForceMode.VelocityChange);
        }
        isLaunched = pelvisBody != null;
        Physics.SyncTransforms();
        Trace("Launch", pos, velocity);
    }

    // Apply remote tracking or the local launch gravity.
    public void FixedTick()
    {
        if (!isRagdoll || pelvisBody == null) return;
        Trace("FixedBegin");
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

    // Preserve the root-space snapshot contract without moving the hierarchy.
    public void UpdateRemoteTarget(Vector3 pos, Quaternion rot, Vector3 velocity)
    {
        if (!isRagdoll || pelvisBody == null) return;
        remoteRootRot = rot;
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

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public void Trace(string phase, Vector3? target = null, Vector3? velocity = null)
    {
        if (traceBuffer == null) return;
        if (Time.realtimeSinceStartup > traceUntil || traceCount >= 2400)
        {
            FlushTrace();
            traceBuffer = null;
            return;
        }

        traceCount++;
        traceBuffer.Append($"[RagdollTrace] player={brain.PlayerId} instance={brain.GetInstanceID()} " +
            $"role={(brain.StateResolver is LocalPlayerStateResolver ? "Local" : "Remote")} " +
            $"phase={phase} frame={Time.frameCount} real={Time.realtimeSinceStartup:F4} " +
            $"utc={System.DateTime.UtcNow:O} time={Time.time:F4} fixed={Time.fixedTime:F4} " +
            $"dt={Time.deltaTime:F4} focus={Application.isFocused} mode={brain.StateResolver.CurrentState.PhysicalMode} " +
            $"root={brain.transform.position.ToString("F4")} " +
            $"anim={brain.Animator.transform.position.ToString("F4")} animEnabled={brain.Animator.enabled}");
        if (brain.Rb != null)
            traceBuffer.Append($" rootRb={brain.Rb.position.ToString("F4")} " +
                $"rootVel={brain.Rb.linearVelocity.ToString("F4")} " +
                $"rootKinematic={brain.Rb.isKinematic} rootInterpolation={brain.Rb.interpolation}");
        if (target.HasValue) traceBuffer.Append($" target={target.Value.ToString("F4")}");
        if (velocity.HasValue) traceBuffer.Append($" suppliedVelocity={velocity.Value.ToString("F4")}");
        foreach (BodyPart part in bodyParts)
        {
            if (part == null || part.Rb == null ||
                (part.Type != BodyPartType.Pelvis && part.Type != BodyPartType.Spine &&
                 part.Type != BodyPartType.Head)) continue;
            traceBuffer.Append($" {part.Type}.rb={part.Rb.position.ToString("F4")} " +
                $"{part.Type}.transform={part.Rb.transform.position.ToString("F4")} " +
                $"{part.Type}.vel={part.Rb.linearVelocity.ToString("F4")} " +
                $"{part.Type}.kinematic={part.Rb.isKinematic}");
        }
        traceBuffer.AppendLine();
        if (Time.realtimeSinceStartup >= traceFlushTime) FlushTrace();
    }

    public void Recover()
    {
        if (!isRagdoll)
        {
            return;
        }

        Trace("RecoverBegin");
        FlushTrace();
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
        recoveryStableTime = 0f;
        isLaunched = false;
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

        if (brain.Rb != null)
        {
            brain.Rb.isKinematic = rootWasKinematic;
            brain.Rb.collisionDetectionMode = rootWasKinematic
                ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.ContinuousDynamic;
            brain.Rb.useGravity = rootUsedGravity;
        }

        Physics.SyncTransforms();
    }

    private static Vector3 GetLaunchAngularVelocity(BodyPartType type, Vector3 tumbleAxis)
    {
        Vector3 angularVelocity = tumbleAxis * LaunchAngularSpeed;
        // Small opposing limb bends break the standing pose without continuous forcing.
        switch (type)
        {
            case BodyPartType.UpperArmLeft:
            case BodyPartType.UpperArmRight:
                angularVelocity += tumbleAxis * LaunchLimbAngularSpeed;
                break;
            case BodyPartType.ForearmLeft:
            case BodyPartType.ForearmRight:
            case BodyPartType.LowerLegLeft:
            case BodyPartType.LowerLegRight:
                angularVelocity -= tumbleAxis * LaunchLimbAngularSpeed;
                break;
        }
        return angularVelocity;
    }

    private void FollowRemotePelvis()
    {
        if (!hasRemoteTarget) return;
        Vector3 correction = Vector3.ClampMagnitude(
            (remotePelvisPos - pelvisBody.position) * RemoteFollowSpeed, RemoteMaxCorrectionSpeed);
        Vector3 velocityChange = remoteVelocity + correction - pelvisBody.linearVelocity;
        Trace("RemoteCorrection", remotePelvisPos, velocityChange);
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

    private void FlushTrace()
    {
        if (traceBuffer == null || traceBuffer.Length == 0) return;
        Debug.Log(traceBuffer.ToString(), brain);
        traceBuffer.Clear();
        traceFlushTime = Time.realtimeSinceStartup + 0.25f;
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
        if (brain.Rb == null || brain.Rb.isKinematic)
        {
            return;
        }

        brain.Rb.linearVelocity = Vector3.zero;
        brain.Rb.angularVelocity = Vector3.zero;
    }
}
