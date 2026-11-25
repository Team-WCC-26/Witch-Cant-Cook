using UnityEngine;

public class PlayerAnimController
{
    private readonly PlayerBrain brain;
    private readonly Animator animator;

    private readonly int speedHash = Animator.StringToHash("Speed");
    private readonly int strafeSpeedHash = Animator.StringToHash("StrafeSpeed");
    private readonly int moveLeftHash = Animator.StringToHash("MoveLeft");
    private readonly int moveRightHash = Animator.StringToHash("MoveRight");
    private readonly int moveForwardHash = Animator.StringToHash("MoveForward");
    private readonly int moveBackwardHash = Animator.StringToHash("MoveBackward");
    private readonly int toIdleHash = Animator.StringToHash("ToIdle");
    private const float DirectionDampTime = 0.1f;
    private Vector2 lastMoveDir = Vector2.up;

    private readonly int heldCategoryHash = Animator.StringToHash("HeldCategory");
    private readonly int holdingCarrotHash = Animator.StringToHash("IsHoldingCarrot");
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

    /// <summary>Update locomotion and held-item animation parameters.</summary>
    public void UpdateTick(PlayerCombinedState state, bool isGrounded, bool isFalling)
    {
        animator.SetInteger(heldCategoryHash, (int)state.HeldEntityCategory);
        CatchableObj held = brain.Interact?.HeldObj;
        animator.SetBool(holdingCarrotHash, held != null &&
            held.Category == EntityCategory.Ingredient && held.GetComponent<IngredientCarrot>() != null);
        animator.SetBool(groundedHash, isGrounded);
        animator.SetBool(fallingHash, isFalling);

        if (state.PhysicalMode != PlayerPhysicalMode.Default)
        {
            ForceIdle();
            return;
        }

        float currentSpeed = brain.ActionController.Movement.CurrentSpeed;

        animator.SetFloat(speedHash, currentSpeed);

        Vector2 moveInput = state.MoveDir;
        if (Mathf.Abs(moveInput.y) > 0.01f)
            lastMoveDir = new Vector2(0f, Mathf.Sign(moveInput.y));
        else if (Mathf.Abs(moveInput.x) > 0.01f)
            lastMoveDir = new Vector2(Mathf.Sign(moveInput.x), 0f);

        // Keep the last direction while movement decelerates.
        Vector2 moveDir = lastMoveDir;
        animator.SetFloat(moveLeftHash, moveDir.x < 0f ? 1f : 0f, DirectionDampTime, Time.deltaTime);
        animator.SetFloat(moveRightHash, moveDir.x > 0f ? 1f : 0f, DirectionDampTime, Time.deltaTime);
        animator.SetFloat(moveForwardHash, moveDir.y > 0f ? 1f : 0f, DirectionDampTime, Time.deltaTime);
        animator.SetFloat(moveBackwardHash, moveDir.y < 0f ? 1f : 0f, DirectionDampTime, Time.deltaTime);

        float runBlend = Mathf.InverseLerp(brain.MoveSpeed,
            brain.MoveSpeed * brain.RunMultiplier, currentSpeed);
        animator.SetFloat(strafeSpeedHash, Mathf.Lerp(1f, 1.5f, runBlend));
    }

    /// <summary>Reset locomotion and immediately return to idle.</summary>
    public void ForceIdle()
    {
        lastMoveDir = Vector2.up;
        animator.SetFloat(moveLeftHash, 0f);
        animator.SetFloat(moveRightHash, 0f);
        animator.SetFloat(moveForwardHash, 1f);
        animator.SetFloat(moveBackwardHash, 0f);
        animator.SetFloat(strafeSpeedHash, 1f);
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

    public void PlayJumpStart()
    {
        // Landing can win over a jump trigger in the same Animator update.
        animator.CrossFadeInFixedTime(jumpStartHash, 0.05f, 0, 0f);
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


