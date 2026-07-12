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
        CatchableObjType heldObjType = ResolveHeldObjType();

        Vector2 moveDir = brain.Input.RawMoveDir;
        bool isRun = brain.Input.RawIsRunning;

        if (physicalMode != PlayerPhysicalMode.Default)
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
            heldObjType
        ));
    }

    public override void FixedTick()
    {
        physicalFSM.FixedTick();

        PlayerPhysicalMode physicalMode = physicalFSM.CurrentMode;

        Vector2 moveDir = CurrentState.MoveDir;
        bool isRun = CurrentState.IsRun;
        CatchableObjType heldObjType = ResolveHeldObjType();
        bool jumpRequested = ConsumeJumpRequest(physicalMode);

        if (physicalMode != PlayerPhysicalMode.Default)
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
            heldObjType,
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

    public override void NotifyCollision(Collision collision)
    {
        physicalFSM.NotifyCollision(collision);
    }

    private void SendMovementPacket()
    {
        PlayerMovementPacket packet = new()
        {
            PlayerId = brain.PlayerId,
            Position = DataConverter.UnityToNumerics(brain.transform.position),
            Rotation = DataConverter.UnityToNumerics(brain.transform.rotation),
            CombinedState = ProtocolTypeConverter.ToProtocolCombinedState(CurrentState)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private CatchableObjType ResolveHeldObjType()
    {
        return brain.Interact.IsHolding
            ? brain.Interact.HeldObj.ObjType
            : CatchableObjType.Default;
    }

    #region Jump State
    // Keeps jump input alive until the next physics tick.
    private void CacheJumpRequest(PlayerPhysicalMode physicalMode)
    {
        if (physicalMode != PlayerPhysicalMode.Default)
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

        pendingJumpRequested |= inputFSM.CurrentJumpRequested;
    }

    // Consumes jump once when physics is ready to apply it.
    private bool ConsumeJumpRequest(PlayerPhysicalMode physicalMode)
    {
        bool jumpRequested =
            physicalMode == PlayerPhysicalMode.Default
            && pendingJumpRequested
            && !isJumpLocked
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

        if (!hasJumpLockStarted)
        {
            hasJumpLockStarted = !brain.ActionController.CanRequestJump;
            return;
        }

        if (brain.ActionController.CanRequestJump)
        {
            isJumpLocked = false;
            hasJumpLockStarted = false;
        }
    }
    #endregion
}
