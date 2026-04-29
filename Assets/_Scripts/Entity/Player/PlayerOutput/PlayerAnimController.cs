using UnityEngine;

public class PlayerAnimController
{
    private readonly PlayerBrain brain;
    private readonly Animator animator;

    private readonly int isMoveHash = Animator.StringToHash("IsMove");

    public PlayerAnimController(PlayerBrain brain)
    {
        this.brain = brain;
        animator = brain.Animator;
    }

    public void UpdateTick(PlayerCombinedState state)
    {
        if (state.PhysicalMode != PlayerPhysicalMode.Default)
        {
            animator.SetBool(isMoveHash, false);
            return;
        }

        bool isMove = state.MoveDir.sqrMagnitude > 0.0001f;
        animator.SetBool(isMoveHash, isMove);
    }
}