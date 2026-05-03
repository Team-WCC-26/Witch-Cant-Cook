using UnityEngine;

public class PlayerActionController
{
    private readonly PlayerBrain brain;

    private readonly PlayerAnimController animController;
    private readonly PlayerRagdollController ragdollController;
    private readonly PlayerMovement movement;

    private PlayerPhysicalMode prevMode;

    public PlayerActionController(PlayerBrain brain)
    {
        this.brain = brain;

        animController = new PlayerAnimController(brain);
        ragdollController = new PlayerRagdollController(brain, animController);
        movement = new PlayerMovement(brain);

        prevMode = PlayerPhysicalMode.Default;
    }

    public void UpdateTick(PlayerCombinedState state)
    {
        // Animator는 Default에서만 의미 있음
        if (state.PhysicalMode == PlayerPhysicalMode.Default)
        {
            animController.UpdateTick(state);
        }

        // 상태 전환 (1회)
        if (state.PhysicalMode != prevMode)
        {
            switch (state.PhysicalMode)
            {
                case PlayerPhysicalMode.Ragdoll:
                    ragdollController.Enter();
                    break;

                case PlayerPhysicalMode.Recover:
                    ragdollController.Recover();
                    break;
            }

            prevMode = state.PhysicalMode;
        }
    }

    public void FixedTick(PlayerCombinedState state)
    {
        if (state.PhysicalMode == PlayerPhysicalMode.Default)
        {
            if (state.MoveDir.sqrMagnitude > 0.0001f)
                movement.Move(state.MoveDir, state.IsRun);
            else
                movement.Stop();
        }
        else
        {
            movement.Stop();
        }
    }
}