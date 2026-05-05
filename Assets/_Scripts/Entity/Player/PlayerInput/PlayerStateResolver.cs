using UnityEngine;

public class PlayerStateResolver
{
    private readonly PlayerBrain brain;
    private readonly PlayerInputFSM inputFSM;
    private readonly PlayerPhysicalFSM physicalFSM;

    public PlayerCombinedState CurrentState { get; private set; }

    public PlayerStateResolver(PlayerBrain brain)
    {
        this.brain = brain;

        inputFSM = new PlayerInputFSM(brain);
        physicalFSM = new PlayerPhysicalFSM(brain);

        CurrentState = new PlayerCombinedState(
            PlayerPhysicalMode.Default,
            Vector2.zero,
            false,
            PlayerInteraction.None
        );
    }

    public void UpdateTick()
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

        CurrentState = new PlayerCombinedState(
            physicalMode,
            moveDir,
            isRun,
            interaction
        );
    }

    public void FixedTick()
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

        CurrentState = new PlayerCombinedState(
            physicalMode,
            moveDir,
            isRun
        );
    }

    public void NotifyCollision(Collision collision)
    {
        physicalFSM.NotifyCollision(collision);
    }
}