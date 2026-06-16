using UnityEngine;

public class PlayerAnimController
{
    private readonly PlayerBrain brain;
    private readonly Animator animator;

    private readonly int speedHash = Animator.StringToHash("Speed");
    private readonly int toIdleHash = Animator.StringToHash("ToIdle");
    private readonly int onHoldHash = Animator.StringToHash("OnHold");

    private const float IdleSpeed = 0f;
    private const float WalkSpeed = 4f;
    private const float RunSpeed = 7f;

    public PlayerAnimController(PlayerBrain brain)
    {
        this.brain = brain;
        animator = brain.Animator;
    }

    public void UpdateTick(PlayerCombinedState state)
    {
        animator.SetBool(onHoldHash, state.HeldObjType != CatchableObjType.Default);

        if (state.PhysicalMode != PlayerPhysicalMode.Default)
        {
            ForceIdle();
            return;
        }

        float currentSpeed = IdleSpeed;

        if (state.MoveDir.sqrMagnitude > 0.0001f)
        {
            currentSpeed = state.IsRun ? RunSpeed : WalkSpeed;
        }

        animator.SetFloat(speedHash, currentSpeed);
    }

    public void ForceIdle()
    {
        animator.SetFloat(speedHash, IdleSpeed);
        animator.SetTrigger(toIdleHash);
        animator.Update(0f);
    }
}
