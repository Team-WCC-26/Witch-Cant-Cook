using Protocol;
using Server;
using UnityEngine;

public sealed class LocalPlayerStateResolver : PlayerStateResolver
{
    private readonly PlayerInputFSM inputFSM;
    private readonly PlayerPhysicalFSM physicalFSM;

    private const float SendInterval = 0.05f;
    private float sendTimer = 0f;
    public LocalPlayerStateResolver(PlayerBrain brain) : base(brain)
    {
        inputFSM = new PlayerInputFSM(brain);
        physicalFSM = new PlayerPhysicalFSM(brain);
    }

    public override void UpdateTick()
    {
        inputFSM.UpdateTick();

        PlayerPhysicalMode physicalMode = CurrentState.PhysicalMode;
        PlayerInteraction interaction = inputFSM.CurrentInteraction;

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
            interaction
        ));
    }

    public override void FixedTick()
    {
        physicalFSM.FixedTick();

        PlayerPhysicalMode physicalMode = physicalFSM.CurrentMode;

        Vector2 moveDir = CurrentState.MoveDir;
        bool isRun = CurrentState.IsRun;

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
            isRun
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
            Position = ProtocolPlayerStateConverter.ToNumericsVector3(brain.transform.position),
            Rotation = ProtocolPlayerStateConverter.ToNumericsVector3(brain.transform.eulerAngles),
            CombinedState = ProtocolPlayerStateConverter.ToProtocolCombinedState(CurrentState)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }
}