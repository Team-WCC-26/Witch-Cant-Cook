using UnityEngine;

public class PlayerAnimController
{
    private readonly PlayerBrain brain;
    private readonly Animator animator;

    private readonly int speedHash = Animator.StringToHash("Speed");
    private readonly int toIdleHash = Animator.StringToHash("ToIdle");

    private readonly float idleSpeed = 0.1f;

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

        Vector3 velocity = brain.Rb.linearVelocity;
        Vector3 horizontalVelocity = new(velocity.x, 0f, velocity.z);

        float currentSpeed = horizontalVelocity.magnitude;

        if (currentSpeed < idleSpeed)
        {
            currentSpeed = 0f;
        }

        animator.SetFloat(speedHash, currentSpeed);
    }

    public void ForceIdle()
    {
        animator.SetFloat(speedHash, 0f);
        animator.SetTrigger(toIdleHash);
        animator.Update(0f);
    }
}