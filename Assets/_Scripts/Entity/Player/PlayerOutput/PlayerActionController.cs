using UnityEngine;

public class PlayerActionController
{
    private readonly PlayerBrain brain;

    private readonly PlayerAnimController animController;
    private readonly PlayerRagdollController ragdollController;
    private readonly PlayerMovement movement;

    private PlayerPhysicalMode prevMode;

    public bool CanRequestJump => movement.IsGroundedNow && !animController.IsJumpMotionPlaying();

    public PlayerActionController(PlayerBrain brain)
    {
        this.brain = brain;

        animController = new PlayerAnimController(brain);
        ragdollController = new PlayerRagdollController(brain, animController);
        movement = new PlayerMovement(brain, brain.MoveSpeed, brain.RunMultiplier, brain.JumpPower);

        prevMode = PlayerPhysicalMode.Default;
    }

    public void UpdateTick(PlayerCombinedState state)
    {
        // Update default locomotion and airborne animation parameters.
        if (state.PhysicalMode == PlayerPhysicalMode.Default)
        {
            animController.UpdateTick(state, movement.IsGroundedNow, movement.VerticalSpeed);
        }

        // Apply physical mode changes once per transition.
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

            ApplyJump(state);
        }
        else
        {
            movement.Stop();
        }
    }

    public void PlayPunch()
    {
        animController.PlayPunch();
    }

    #region Jump Action
    // Applies one-shot jump requests after horizontal movement is resolved.
    private void ApplyJump(PlayerCombinedState state)
    {
        if (!state.JumpRequested) return;
        movement.Jump();
    }
    #endregion
}
