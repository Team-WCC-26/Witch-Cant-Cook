using UnityEngine;

public class PlayerActionController
{
    private readonly PlayerBrain brain;

    private readonly PlayerAnimController animController;
    private readonly PlayerRagdollController ragdollController;
    private readonly PlayerMovement movement;

    private PlayerPhysicalMode prevMode;

    public bool CanRequestJump => movement.IsGroundedNow && !animController.IsJumpMotionPlaying();
    public bool CanPunch { get; private set; }
    private float punchTime = 0f;

    
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
        UpdatePunchState();

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

    #region Punch Action
    public void PlayPunch()
    {
        if (!CanPunch) return;

        animController.PlayPunch();
        CanPunch = false;
    }

    private void UpdatePunchState()
    {
        bool isPlaying = animController.IsPunchMotionPlaying();
        if (isPlaying) return;

        if (!CanPunch)
        {
            if (punchTime == 0)
                punchTime = brain.PunchRecoveryDelay;
            else
                punchTime -= Time.deltaTime;

            if (punchTime <= 0)
            {
                CanPunch = true;
                punchTime = 0;
            }
        }
    }
    #endregion

    #region Jump Action
    // Applies one-shot jump requests after horizontal movement is resolved.
    private void ApplyJump(PlayerCombinedState state)
    {
        if (!state.JumpRequested) return;
        movement.Jump();
    }
    #endregion
}
