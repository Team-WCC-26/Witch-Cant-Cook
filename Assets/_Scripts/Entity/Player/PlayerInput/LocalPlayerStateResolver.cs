using Protocol;
using Server;
using UnityEngine;

public sealed class LocalPlayerStateResolver : PlayerStateResolver
{
    private readonly PlayerInputFSM inputFSM;
    private readonly PlayerPhysicalFSM physicalFSM;

    private const float SendInterval = 0.05f;
    private float sendTimer = 0f;
    private bool pendingJumpRequested = false;
    private bool isJumpLocked = false;
    private bool hasJumpLockStarted = false;
    private float nextJumpAllowedTime;
    public LocalPlayerStateResolver(PlayerBrain brain) : base(brain)
    {
        inputFSM = new PlayerInputFSM(brain);
        physicalFSM = new PlayerPhysicalFSM(brain);
    }

    public override void UpdateTick()
    {
        inputFSM.UpdateTick();

        PlayerPhysicalMode physicalMode = CurrentState.PhysicalMode;
        UpdateJumpLock(physicalMode);
        CacheJumpRequest(physicalMode);

        PlayerInteraction interaction = inputFSM.CurrentInteraction;
        EntityCategory heldEntityCategory = ResolveHeldEntityCategory();

        Vector2 moveDir = brain.Input.RawMoveDir;
        bool isRun = brain.Input.RawIsRunning;

        if (physicalMode != PlayerPhysicalMode.Default || brain.Input.IsInputBlocked)
        {
            moveDir = Vector2.zero;
            isRun = false;
            interaction = PlayerInteraction.None;
        }

        if (moveDir.sqrMagnitude <= 0.0001f)
        {
            moveDir = Vector2.zero;
            isRun = false;
        }

        SetCurrentState(new PlayerCombinedState(
            physicalMode,
            moveDir,
            isRun,
            interaction,
            heldEntityCategory
        ));
    }

    public override void FixedTick()
    {
        physicalFSM.FixedTick();

        PlayerPhysicalMode physicalMode = physicalFSM.CurrentMode;

        Vector2 moveDir = CurrentState.MoveDir;
        bool isRun = CurrentState.IsRun;
        EntityCategory heldEntityCategory = ResolveHeldEntityCategory();
        bool jumpRequested = ConsumeJumpRequest(physicalMode);

        if (physicalMode != PlayerPhysicalMode.Default || brain.Input.IsInputBlocked)
        {
            moveDir = Vector2.zero;
            isRun = false;
        }

        if (moveDir.sqrMagnitude <= 0.0001f)
        {
            moveDir = Vector2.zero;
            isRun = false;
        }

        SetCurrentState(new PlayerCombinedState(
            physicalMode,
            moveDir,
            isRun,
            PlayerInteraction.None,
            heldEntityCategory,
            jumpRequested
        ));

        sendTimer += Time.fixedDeltaTime;

        if (sendTimer < SendInterval)
        {
            return;
        }

        sendTimer = 0f;
        SendMovementPacket();
    }

    public void EnterRagdoll()
    {
        physicalFSM.EnterRagdoll();
    }

    private void SendMovementPacket()
    {
        PlayerCombinedState outgoingState = CurrentState;
        if (brain.ActionController.ConsumePrimaryAction(out EntityCategory category)
            && CurrentState.PhysicalMode == PlayerPhysicalMode.Default)
        {
            // Send the action that actually played, once, without changing local input state.
            outgoingState = new PlayerCombinedState(
                CurrentState.PhysicalMode,
                CurrentState.MoveDir,
                CurrentState.IsRun,
                category == EntityCategory.Player
                    ? PlayerInteraction.DefaultPrimary
                    : PlayerInteraction.HeldPrimary,
                category,
                CurrentState.JumpRequested
            );
        }

        Quaternion facingRotation = brain.CameraController != null
            ? brain.CameraController.YawRotation
            : brain.transform.rotation;

        PlayerMovementPacket packet = new()
        {
            PlayerId = brain.PlayerId,
            Position = DataConverter.UnityToNumerics(brain.transform.position),
            Rotation = DataConverter.UnityToNumerics(facingRotation),
            CombinedState = ProtocolTypeConverter.ToProtocolCombinedState(outgoingState)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private EntityCategory ResolveHeldEntityCategory()
    {
        return brain.Interact.IsHolding
            ? brain.Interact.HeldObj.Category
            : EntityCategory.Player;
    }

    #region Jump State
    // Keeps jump input alive until the next physics tick.
    private void CacheJumpRequest(PlayerPhysicalMode physicalMode)
    {
        if (physicalMode != PlayerPhysicalMode.Default || brain.Input.IsInputBlocked)
        {
            pendingJumpRequested = false;
            isJumpLocked = false;
            hasJumpLockStarted = false;
            return;
        }

        if (isJumpLocked)
        {
            return;
        }

        pendingJumpRequested |= inputFSM.CurrentJumpRequested ||
            (brain.Input.RawIsJumpHeld && brain.ActionController.IsGroundedNow);
    }

    // Consumes jump once when physics is ready to apply it.
    private bool ConsumeJumpRequest(PlayerPhysicalMode physicalMode)
    {
        bool jumpRequested =
            physicalMode == PlayerPhysicalMode.Default
            && !brain.Input.IsInputBlocked
            && pendingJumpRequested
            && !isJumpLocked
            && Time.time >= nextJumpAllowedTime
            && !brain.ActionController.Movement.IsForcedMovementActive
            && brain.ActionController.CanRequestJump;

        if (jumpRequested)
        {
            isJumpLocked = true;
            hasJumpLockStarted = false;
        }

        pendingJumpRequested = false;
        return jumpRequested;
    }

    private void UpdateJumpLock(PlayerPhysicalMode physicalMode)
    {
        if (!isJumpLocked) return;

        if (physicalMode != PlayerPhysicalMode.Default)
        {
            isJumpLocked = false;
            hasJumpLockStarted = false;
            return;
        }

        bool isGrounded = brain.ActionController.IsGroundedNow;
        if (!hasJumpLockStarted)
        {
            hasJumpLockStarted = !isGrounded;
            return;
        }

        if (isGrounded)
        {
            isJumpLocked = false;
            hasJumpLockStarted = false;
            // Repeat delay starts at landing, not at the previous takeoff.
            nextJumpAllowedTime = Time.time + brain.JumpRepeatDelay;
        }
    }
    #endregion
}
