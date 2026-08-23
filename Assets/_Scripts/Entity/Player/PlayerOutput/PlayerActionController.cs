using UnityEngine;

public class PlayerActionController
{
    private readonly PlayerBrain brain;

    private readonly PlayerAnimController animController;
    private readonly PlayerRagdollController ragdollController;
    private readonly PlayerMovement movement;

    private PlayerPhysicalMode prevMode;

    public PlayerMovement Movement => movement;
    public bool CanRequestJump => movement.CanJump;
    public bool IsGroundedNow => movement.IsGroundedNow;

    private bool canAction = true;
    private float actionTime;


    public PlayerActionController(PlayerBrain brain)
    {
        this.brain = brain;

        animController = new PlayerAnimController(brain);
        ragdollController = new PlayerRagdollController(brain, animController);
        movement = new PlayerMovement(brain, brain.MoveSpeed, brain.RunMultiplier, brain.JumpPower, brain.Acceleration, brain.Deceleration);
        prevMode = PlayerPhysicalMode.Default;
    }

    public void UpdateTick(PlayerCombinedState state)
    {
        UpdateActionState();
        movement.UpdateGroundState();

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
                case PlayerPhysicalMode.Default when prevMode == PlayerPhysicalMode.Ragdoll:
                    ragdollController.Recover();
                    break;

                case PlayerPhysicalMode.Ragdoll:
                    brain.Interact.ForceDropHeld();
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
            // 입력이 없어도 Move(0, ...)를 호출해 마찰(FrictionMultiplier) 기반 감속을 타게 한다.
            // Stop()으로 바로 가면 즉시 0으로 스냅되어 빙판 등의 미끄러짐 효과가 무시된다.
            movement.Move(state.MoveDir, state.IsRun);
            ApplyJump(state);
            movement.ApplyFallGravity();
        }
        else
        {
            movement.Stop();
        }
    }

    #region Action
    public void PunchAction()
    {
        if (!canAction) return;

        animController.PlayPunchAnim();
        canAction = false;
    }

    public bool TryEquipAction()
    {
        if (!canAction) return false;

        animController.PlayEquipAnim();
        canAction = false;
        return true;
    }

    private void UpdateActionState()
    {
        bool isPlaying =
            animController.IsPunchMotionPlaying()
            || animController.IsEquipActionPlaying();

        if (isPlaying) return;

        if (canAction) return;

        if (actionTime == 0)
            actionTime = brain.RecoveryDelay;
        else
            actionTime -= Time.deltaTime;

        if (actionTime > 0) return;

        canAction = true;
        actionTime = 0;
    }

    public void CancelAction()
    {
        animController.CancelAction();
        canAction = true;
        actionTime = 0;
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
