using UnityEngine;

public class PlayerAnimController
{
    private readonly PlayerBrain brain;
    private readonly Animator animator;

    private readonly int speedHash = Animator.StringToHash("Speed");
    private readonly int toIdleHash = Animator.StringToHash("ToIdle");

    private readonly int heldCategoryHash = Animator.StringToHash("HeldCategory");
    private readonly int primaryActionHash = Animator.StringToHash("Primary Action");
    private readonly int jumpHash = Animator.StringToHash("Jump");
    
    private readonly int groundedHash = Animator.StringToHash("IsGrounded");
    private readonly int fallingHash = Animator.StringToHash("IsFalling");
    private readonly int jumpStartHash = Animator.StringToHash("JumpStart");
    private readonly int fallHash = Animator.StringToHash("Fall");
    private readonly int jumpEndHash = Animator.StringToHash("JumpEnd");

    private readonly int emptyStateHash = Animator.StringToHash("Empty");
    private readonly int[] primaryActionStateHashes =
    {
        Animator.StringToHash("Punch Action"),
        Animator.StringToHash("Ingredient Action"),
        Animator.StringToHash("Pan Action"),
        Animator.StringToHash("Knife Action"),
        Animator.StringToHash("Plate Action"),
        Animator.StringToHash("Broom Action"),
        Animator.StringToHash("Bucket Action"),
        Animator.StringToHash("Default Action")
    };

    public PlayerAnimController(PlayerBrain brain)
    {
        this.brain = brain;
        animator = brain.Animator;
    }

    public void UpdateTick(PlayerCombinedState state, bool isGrounded, bool isFalling)
    {
        animator.SetInteger(heldCategoryHash, (int)state.HeldEntityCategory);
        animator.SetBool(groundedHash, isGrounded);
        animator.SetBool(fallingHash, isFalling);

        if (state.PhysicalMode != PlayerPhysicalMode.Default)
        {
            ForceIdle();
            return;
        }

        float currentSpeed = brain.ActionController.Movement.CurrentSpeed;

        animator.SetFloat(speedHash, currentSpeed);
    }

    public void ForceIdle()
    {
        animator.SetFloat(speedHash, 0f);
        animator.SetTrigger(toIdleHash);
        animator.Update(0f);
    }

    public void PlayPrimaryAction()
    {
        animator.SetTrigger(primaryActionHash);
    }

    public void PlayJumpAnim()
    {
        animator.SetTrigger(jumpHash);
    }

    public void CancelAction()
    {
        const int UpperBodyLayer = 1;

        animator.ResetTrigger(primaryActionHash);
        animator.CrossFade(emptyStateHash, 0f, UpperBodyLayer);
    }

    public bool IsPrimaryActionPlaying()
    {
        const int UpperBodyLayer = 1;

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(UpperBodyLayer);
        if (IsPrimaryActionState(current.shortNameHash)) return true;
        if (!animator.IsInTransition(UpperBodyLayer)) return false;

        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(UpperBodyLayer);
        return IsPrimaryActionState(next.shortNameHash);
    }

    private bool IsPrimaryActionState(int stateHash)
    {
        foreach (int actionStateHash in primaryActionStateHashes)
        {
            if (stateHash == actionStateHash) return true;
        }

        return false;
    }

    public bool IsJumpMotionPlaying()
    {
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);

        if (IsActiveJumpState(currentState)) return true;
        if (!animator.IsInTransition(0)) return false;

        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        return IsActiveJumpState(nextState);
    }

    private bool IsActiveJumpState(AnimatorStateInfo stateInfo)
    {
        int stateHash = stateInfo.shortNameHash;

        if (stateHash == jumpEndHash)
        {
            return stateInfo.normalizedTime < 1f;
        }

        return stateHash == jumpStartHash || stateHash == fallHash;
    }
}


