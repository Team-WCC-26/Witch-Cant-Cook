using Protocol;
using UnityEngine;

public sealed class RemotePlayerStateResolver : PlayerStateResolver
{
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool hasRemoteTransform = false;
    private PlayerInteraction previousRemoteInteraction = PlayerInteraction.None;

    private float positionLerpSpeed = 15f;
    private float rotationLerpSpeed = 15f;

    public float VerticalSpeed { get; private set; }

    private const float JumpStartMinSpeed = 1.5f;
    private const float JumpStartMinRise = 0.15f;
    private const float JumpRearmMinFallSpeed = 1.5f;
    private const float JumpRearmMinDrop = 0.15f;
    private const float LandingConfirmTime = 0.08f;
    private bool hasJumpBaseline;
    private float riseStartHeight;
    private float jumpPeakHeight;
    private bool jumpStartTriggered;
    private bool hasLeftGround;
    private float groundedTime;

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
            return;
        }

        float previousHeight = brain.transform.position.y;
        brain.transform.position = Vector3.Lerp(
            brain.transform.position,
            targetPosition,
            Time.deltaTime * positionLerpSpeed
        );

        // Transform interpolation, rather than Rigidbody physics, moves remote players.
        VerticalSpeed = Time.deltaTime > 0f
            ? (brain.transform.position.y - previousHeight) / Time.deltaTime
            : 0f;

        brain.transform.rotation = Quaternion.Slerp(
            brain.transform.rotation,
            targetRotation,
            Time.deltaTime * rotationLerpSpeed
        );

        UpdateRemoteJump(previousHeight);
    }

    private void UpdateRemoteJump(float previousHeight)
    {
        float height = brain.transform.position.y;
        bool isGrounded = brain.ActionController.IsGroundedNow;

        if (!hasJumpBaseline || CurrentState.PhysicalMode != PlayerPhysicalMode.Default)
        {
            hasJumpBaseline = true;
            riseStartHeight = height;
            jumpPeakHeight = height;
            jumpStartTriggered = false;
            hasLeftGround = false;
            groundedTime = 0f;
            return;
        }

        if (!isGrounded) hasLeftGround = true;

        if (jumpStartTriggered)
        {
            jumpPeakHeight = Mathf.Max(jumpPeakHeight, height);
            // A clear descent rearms remote jumps even when interpolation skips landing.
            if (VerticalSpeed <= -JumpRearmMinFallSpeed &&
                jumpPeakHeight - height >= JumpRearmMinDrop)
            {
                jumpStartTriggered = false;
                riseStartHeight = height;
            }
        }

        if (hasLeftGround && isGrounded && VerticalSpeed <= 0f)
        {
            groundedTime += Time.deltaTime;
            if (groundedTime >= LandingConfirmTime)
            {
                jumpStartTriggered = false;
                hasLeftGround = false;
                riseStartHeight = height;
            }
        }
        else groundedTime = 0f;

        if (VerticalSpeed <= 0f)
            riseStartHeight = height;
        else
            riseStartHeight = Mathf.Min(riseStartHeight, previousHeight);

        if (!jumpStartTriggered && VerticalSpeed >= 0.5f)
        {
            jumpStartTriggered = true;
            jumpPeakHeight = height;
            brain.ActionController.PlayRemoteJumpStart();
        }
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
        targetPosition = DataConverter.NumericsToUnity(position);
        targetRotation = Quaternion.Euler(ProtocolTypeConverter.ToUnityVector3(rotation));
        hasRemoteTransform = true;
    }

    public void ApplyRemoteTransform(System.Numerics.Vector3 position, System.Numerics.Quaternion rotation)
    {
        targetPosition = DataConverter.NumericsToUnity(position);
        targetRotation = DataConverter.NumericsToUnity(rotation);
        hasRemoteTransform = true;
    }

    public void ApplyRemoteTransform(Vector3 position, Vector3 eulerAngles)
    {
        targetPosition = position;
        targetRotation = Quaternion.Euler(eulerAngles);
        hasRemoteTransform = true;
    }

    public void SetLerpSpeed(float positionSpeed, float rotationSpeed)
    {
        positionLerpSpeed = positionSpeed;
        rotationLerpSpeed = rotationSpeed;
    }
}
