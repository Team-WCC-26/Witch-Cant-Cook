using Protocol;
using Server;
using UnityEngine;
using Cysharp.Threading.Tasks;

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
    private float rocketEndSnapshotUntil;
    private Vector3 rocketEndPosition;
    private Quaternion rocketEndRotation;
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
        _ = SendMovementPacket();
    }

    public void EnterRagdoll()
    {
        physicalFSM.EnterRagdoll();
    }

    public void SetRagdollRecoveryPaused(bool paused)
    {
        physicalFSM.SetRecoveryPaused(paused);
    }

    public void SendRocketSnapshot(bool ended)
    {
        // Keep the terminal pose for two send intervals so batching preserves short flights.
        rocketEndSnapshotUntil = ended ? Time.time + SendInterval * 2f : 0f;
        if (ended)
        {
            rocketEndPosition = brain.Rb != null ? brain.Rb.position : brain.transform.position;
            rocketEndRotation = Quaternion.LookRotation(brain.Rocket.FlightDirection, Vector3.up);
        }
        if (ServerManager.Instance == null) return;
        sendTimer = 0f;
        _ = SendMovementPacket();
    }

    public UniTask SendPanEject()
    {
        return SendMovementPacket(true);
    }

    private UniTask SendMovementPacket(bool ejectPan = false)
    {
        PlayerCombinedState outgoingState = CurrentState;
        if (ejectPan)
        {
            // A one-shot Drop in the movement snapshot is the existing server pan-eject command.
            outgoingState = new PlayerCombinedState(
                CurrentState.PhysicalMode,
                CurrentState.MoveDir,
                CurrentState.IsRun,
                PlayerInteraction.HeldPrimary,
                EntityCategory.Pan,
                CurrentState.JumpRequested);
        }
        else if (brain.ActionController.ConsumePrimaryAction(out EntityCategory category)
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
        Vector3 position = outgoingState.PhysicalMode == PlayerPhysicalMode.Ragdoll || brain.Rb == null
            ? brain.transform.position : brain.Rb.position;

        // Reuse the existing special-action snapshot marker; no new wire format is required.
        bool endingRocket = Time.time < rocketEndSnapshotUntil;
        if (brain.Rocket != null && (brain.Rocket.IsFlying || endingRocket) &&
            brain.Interact.HeldObj != null && brain.Interact.HeldObj.GetComponent<IngredientCarrot>() != null &&
            CurrentState.PhysicalMode == PlayerPhysicalMode.Default)
        {
            outgoingState = new PlayerCombinedState(CurrentState.PhysicalMode,
                Vector2.zero, false, PlayerInteraction.SpecialInteract, EntityCategory.Ingredient);
            facingRotation = endingRocket ? rocketEndRotation : Quaternion.LookRotation(brain.Rocket.FlightDirection, Vector3.up);
            if (endingRocket) position = rocketEndPosition;
        }

        PlayerMovementPacket packet = new()
        {
            PlayerId = brain.PlayerId,
            Position = DataConverter.UnityToNumerics(position),
            Rotation = DataConverter.UnityToNumerics(facingRotation),
            CombinedState = ProtocolTypeConverter.ToProtocolCombinedState(outgoingState)
        };

        return ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
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
        if (brain.Rocket != null && brain.Rocket.IsFlying)
        {
            pendingJumpRequested = false;
            return;
        }
        if (physicalMode != PlayerPhysicalMode.Default || brain.Input.IsInputBlocked || brain.Input.IsMovementBlocked)
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
            && !brain.Input.IsInputBlocked && !brain.Input.IsMovementBlocked
            && pendingJumpRequested
            && !isJumpLocked
            && Time.time >= nextJumpAllowedTime
            && !brain.ActionController.Movement.IsForcedMovementActive
            && !(brain.Rocket != null && brain.Rocket.IsFlying)
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
