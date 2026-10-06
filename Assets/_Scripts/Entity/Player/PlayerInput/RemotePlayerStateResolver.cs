using Protocol;
using UnityEngine;

public sealed class RemotePlayerStateResolver : PlayerStateResolver
{
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool hasRemoteTransform = false;
    private float lastSnapshotTime;
    private float snapshotVerticalSpeed;
    private PlayerInteraction previousRemoteInteraction = PlayerInteraction.None;

    private float positionLerpSpeed = 15f;
    private float rotationLerpSpeed = 15f;

    public float VerticalSpeed { get; private set; }
    public bool IsGroundedForAnimation { get; private set; }

    private const float JumpStartMinSpeed = 1.5f;
    private const float JumpStartMinRise = 0.03f;
    private const float GroundedSnapshotClearance = 0.045f;
    private const float NearGroundDescentClearance = 0.25f;
    private const float LandingSettleSpeed = 12f;
    private const float RemoteLandingHeightTolerance = 0.12f;
    private const float RemoteLandingHorizontalTolerance = 0.5f;
    private const float MaxSnapshotVerticalSpeed = 20f;
    private const float MinSnapshotInterval = 0.01f;
    // Source packet jump cycle is independent of the interpolated visual transform.
    private bool jumpArmed;
    private bool jumpConsumed;
    private bool pendingJumpStart;
    private float jumpFloorHeight;
    private float lastDescendingClearance = float.PositiveInfinity;
    private float lastDescendingSpeed;

    public RemotePlayerStateResolver(PlayerBrain brain) : base(brain)
    {
        targetPosition = brain.transform.position;
        targetRotation = brain.transform.rotation;

        if (brain.Rb != null)
        {
            brain.Rb.isKinematic = true;
            brain.Rb.useGravity = false;
            brain.Rb.linearVelocity = Vector3.zero;
            brain.Rb.angularVelocity = Vector3.zero;
        }
    }

    public override void UpdateTick()
    {
        if (!hasRemoteTransform)
        {
            VerticalSpeed = 0f;
            IsGroundedForAnimation = brain.ActionController.IsGroundedNow;
            return;
        }

        Vector3 previousPosition = brain.transform.position;
        Vector3 nextPosition = Vector3.Lerp(
            previousPosition,
            targetPosition,
            Time.deltaTime * positionLerpSpeed
        );

        // Follow measured packet descent without the slow tail of vertical Lerp.
        if (CurrentState.PhysicalMode == PlayerPhysicalMode.Default &&
            targetPosition.y < previousPosition.y && snapshotVerticalSpeed < 0f)
        {
            float verticalFollowSpeed = Mathf.Max(
                -snapshotVerticalSpeed,
                (previousPosition.y - targetPosition.y) * positionLerpSpeed
            );
            nextPosition.y = Mathf.MoveTowards(
                previousPosition.y, targetPosition.y,
                verticalFollowSpeed * Time.deltaTime
            );
        }

        if (pendingJumpStart)
        {
            // Start the launch animation at the floor even when the visual
            // transform still trails the source player's previous landing.
            nextPosition.y = Mathf.MoveTowards(
                previousPosition.y, jumpFloorHeight, LandingSettleSpeed * Time.deltaTime
            );
        }

        brain.transform.position = nextPosition;
        brain.transform.rotation = Quaternion.Slerp(
            brain.transform.rotation,
            targetRotation,
            Time.deltaTime * rotationLerpSpeed
        );

        bool targetGrounded = IsTargetGrounded();
        Vector3 remaining = targetPosition - nextPosition;
        remaining.y = 0f;
        bool landingWithinReach = CurrentState.PhysicalMode == PlayerPhysicalMode.Default &&
            targetGrounded &&
            Mathf.Abs(nextPosition.y - targetPosition.y) <= RemoteLandingHeightTolerance &&
            remaining.sqrMagnitude <= RemoteLandingHorizontalTolerance * RemoteLandingHorizontalTolerance;

        // Finish the last part of a confirmed descent together with the landing animation.
        if (landingWithinReach && targetPosition.y <= previousPosition.y && !pendingJumpStart)
        {
            nextPosition.y = targetPosition.y;
            brain.transform.position = nextPosition;
        }

        bool launchAtFloor = pendingJumpStart &&
            Mathf.Abs(nextPosition.y - jumpFloorHeight) <= 0.02f;
        if (launchAtFloor)
        {
            nextPosition.y = jumpFloorHeight;
            brain.transform.position = nextPosition;
            pendingJumpStart = false;
        }

        VerticalSpeed = Time.deltaTime > 0f
            ? (nextPosition.y - previousPosition.y) / Time.deltaTime
            : 0f;
        IsGroundedForAnimation = launchAtFloor ||
            brain.ActionController.IsGroundedNow ||
            (VerticalSpeed <= 0f && landingWithinReach);

        if (launchAtFloor) brain.ActionController.PlayRemoteJumpStart();
    }

    private bool TryGetGroundClearance(Vector3 position, out float clearance)
    {
        clearance = 0f;
        if (brain.Col == null) return false;

        Bounds bounds = brain.Col.bounds;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
        float centerToBottom = bounds.extents.y - radius;
        Vector3 center = bounds.center + (position - brain.transform.position);

        if (!Physics.SphereCast(
                center, radius, Vector3.down, out RaycastHit hit,
                centerToBottom + 2f, brain.GroundLayerMask,
                QueryTriggerInteraction.Ignore
            )) return false;

        clearance = Mathf.Max(0f, hit.distance - centerToBottom);
        return true;
    }

    private void ObserveJumpSnapshot(Vector3 position, float sampleInterval)
    {
        if (CurrentState.PhysicalMode != PlayerPhysicalMode.Default)
        {
            jumpArmed = false;
            jumpConsumed = false;
            pendingJumpStart = false;
            lastDescendingClearance = float.PositiveInfinity;
            return;
        }

        float deltaY = hasRemoteTransform ? position.y - targetPosition.y : 0f;
        bool rising = hasRemoteTransform &&
            deltaY >= JumpStartMinRise &&
            snapshotVerticalSpeed >= JumpStartMinSpeed;

        // A high packet can miss the floor cast without losing an armed jump.
        if (!TryGetGroundClearance(position, out float clearance))
        {
            if (rising && jumpArmed && !pendingJumpStart)
            {
                jumpArmed = false;
                jumpConsumed = true;
                pendingJumpStart = true;
                lastDescendingClearance = float.PositiveInfinity;
            }
            return;
        }


        // A source-side ground sample rearms the next jump even when the
        // displayed avatar has not completed its descent.
        if (clearance <= GroundedSnapshotClearance && !rising)
        {
            jumpArmed = true;
            jumpConsumed = false;
            jumpFloorHeight = position.y - clearance;
            lastDescendingClearance = float.PositiveInfinity;
        }

        bool inferredLanding = jumpConsumed &&
            lastDescendingClearance <= NearGroundDescentClearance &&
            lastDescendingSpeed >= JumpStartMinSpeed &&
            sampleInterval >= lastDescendingClearance / lastDescendingSpeed + 0.01f;

        // A rise while still touching a higher floor is movement, not a jump.
        if (rising && clearance > GroundedSnapshotClearance &&
            (jumpArmed || inferredLanding) && !pendingJumpStart)
        {
            jumpFloorHeight = position.y - clearance;
            jumpArmed = false;
            jumpConsumed = true;
            pendingJumpStart = true;
            lastDescendingClearance = float.PositiveInfinity;
        }
        else if (hasRemoteTransform && snapshotVerticalSpeed <= -JumpStartMinSpeed &&
                 clearance <= NearGroundDescentClearance)
        {
            lastDescendingClearance = clearance;
            lastDescendingSpeed = -snapshotVerticalSpeed;
        }
    }

    private bool IsTargetGrounded()
    {
        if (brain.Col == null) return false;

        Bounds bounds = brain.Col.bounds;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
        float distance = bounds.extents.y - radius + brain.GroundCheckDistance;
        Vector3 targetCenter = bounds.center + (targetPosition - brain.transform.position);

        return Physics.SphereCast(
            targetCenter, radius, Vector3.down, out _, distance,
            brain.GroundLayerMask, QueryTriggerInteraction.Ignore
        );
    }

    public override void FixedTick()
    {
    }

    public override void ApplyRemotePacket(PlayerMovementPacket packet)
    {
        if (packet == null) return;

        ApplyRemoteState(
            ProtocolTypeConverter.ToClientCombinedState(packet.CombinedState)
        );

        ApplyRemoteTransform(packet.Position, packet.Rotation);
    }

    public void ApplyRemoteState(PlayerCombinedState remoteState)
    {
        SetCurrentState(remoteState);

        // Repeated snapshots must not restart the same action every frame.
        bool isPrimary = remoteState.Interaction == PlayerInteraction.DefaultPrimary
            || remoteState.Interaction == PlayerInteraction.HeldPrimary;
        if (isPrimary && remoteState.Interaction != previousRemoteInteraction)
        {
            brain.ActionController.PlayRemotePrimaryAction(remoteState);
        }

        previousRemoteInteraction = remoteState.Interaction;
    }

    public void ApplyRemoteTransform(System.Numerics.Vector3 position, System.Numerics.Vector3 rotation)
    {
        SetRemoteTarget(
            DataConverter.NumericsToUnity(position),
            Quaternion.Euler(ProtocolTypeConverter.ToUnityVector3(rotation))
        );
    }

    public void ApplyRemoteTransform(System.Numerics.Vector3 position, System.Numerics.Quaternion rotation)
    {
        SetRemoteTarget(DataConverter.NumericsToUnity(position), DataConverter.NumericsToUnity(rotation));
    }

    public void ApplyRemoteTransform(Vector3 position, Vector3 eulerAngles)
    {
        SetRemoteTarget(position, Quaternion.Euler(eulerAngles));
    }

    private void SetRemoteTarget(Vector3 position, Quaternion rotation)
    {
        float now = Time.time;
        float sampleInterval = now - lastSnapshotTime;
        if (hasRemoteTransform)
        {
            snapshotVerticalSpeed = Mathf.Clamp(
                (position.y - targetPosition.y) / Mathf.Max(sampleInterval, MinSnapshotInterval),
                -MaxSnapshotVerticalSpeed, MaxSnapshotVerticalSpeed
            );
        }

        ObserveJumpSnapshot(position, sampleInterval);
        lastSnapshotTime = now;
        targetPosition = position;
        targetRotation = rotation;
        hasRemoteTransform = true;
    }


    public void SetLerpSpeed(float positionSpeed, float rotationSpeed)
    {
        positionLerpSpeed = positionSpeed;
        rotationLerpSpeed = rotationSpeed;
    }
}
