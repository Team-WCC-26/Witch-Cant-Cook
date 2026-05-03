using UnityEngine;

public class PlayerAnimController
{
    private readonly PlayerBrain brain;
    private readonly Animator animator;

    private readonly int isMoveHash = Animator.StringToHash("IsMove");
    private readonly int toIdleHash = Animator.StringToHash("ToIdle");

    public PlayerAnimController(PlayerBrain brain)
    {
        this.brain = brain;
        animator = brain.Animator;
    }

    public void UpdateTick(PlayerCombinedState state)
    {
        if (state.PhysicalMode != PlayerPhysicalMode.Default)
        {
            ForceIdle();
            return;
        }

        bool isMove = state.MoveDir.sqrMagnitude > 0.0001f;
        animator.SetBool(isMoveHash, isMove);
    }

    public void ForceIdle()
    {
        animator.SetBool(isMoveHash, false);
        animator.SetTrigger(toIdleHash);
        animator.Update(0f);
    }
}