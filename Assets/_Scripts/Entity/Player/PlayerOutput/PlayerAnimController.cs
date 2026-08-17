using UnityEngine;

public class PlayerAnimController
{
    private readonly PlayerBrain brain;
    private readonly Animator animator;

    private readonly int speedHash = Animator.StringToHash("Speed");
    private readonly int toIdleHash = Animator.StringToHash("ToIdle");

    private readonly int onHoldHash = Animator.StringToHash("OnHold");
    private readonly int equipActionHash = Animator.StringToHash("EquipAction");
    private readonly int punchHash = Animator.StringToHash("Punch");
    
    private readonly int groundedHash = Animator.StringToHash("IsGrounded");
    private readonly int vSpeedHash = Animator.StringToHash("VSpeed");
    private readonly int jumpStartHash = Animator.StringToHash("JumpStart");
    private readonly int jumpMiddleHash = Animator.StringToHash("JumpMiddle");
    private readonly int jumpEndHash = Animator.StringToHash("JumpEnd");

    private readonly int emptyStateHash = Animator.StringToHash("Empty");
    private readonly int equipActionStateHash = Animator.StringToHash("HoldKnife");
    private readonly int punchStateHash = Animator.StringToHash("Attack_hand_1_(left)");

    private const float IdleSpeed = 0f;
    private const float WalkSpeed = 4f;
    private const float RunSpeed = 7f;

    public PlayerAnimController(PlayerBrain brain)
    {
        this.brain = brain;
        animator = brain.Animator;
    }

    public void UpdateTick(PlayerCombinedState state, bool isGrounded, float vSpeed)
    {
        bool isHolding = state.HeldObjType != CatchableObjType.Default;
        bool isEquipped = brain.Interact.IsHolding && brain.Interact.HeldObj.IsEquipment;

        animator.SetBool(onHoldHash, isHolding && !isEquipped);
        animator.SetBool(groundedHash, isGrounded);
        animator.SetFloat(vSpeedHash, vSpeed);

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

    public void PlayPunchAnim()
    {
        animator.SetTrigger(punchHash);
    }

    public void PlayEquipAnim()
    {
        animator.SetTrigger(equipActionHash);
    }

    public void CancelAction()
    {
        const int UpperBodyLayer = 1;

        animator.ResetTrigger(punchHash);
        animator.ResetTrigger(equipActionHash);
        animator.CrossFade(emptyStateHash, 0f, UpperBodyLayer);
    }

    public bool IsEquipActionPlaying()
    {
        const int EquipmentLayer = 1;

        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(EquipmentLayer);
        if (current.shortNameHash == equipActionStateHash) return true;
        if (!animator.IsInTransition(EquipmentLayer)) return false;

        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(EquipmentLayer);
        return next.shortNameHash == equipActionStateHash;
    }

    public bool IsPunchMotionPlaying()
    {
        const int UpperBodyLayer = 1;

        AnimatorStateInfo current =
            animator.GetCurrentAnimatorStateInfo(UpperBodyLayer);

        if (current.shortNameHash == punchStateHash)
            return true;

        if (!animator.IsInTransition(UpperBodyLayer))
            return false;

        AnimatorStateInfo next =
            animator.GetNextAnimatorStateInfo(UpperBodyLayer);

        return next.shortNameHash == punchStateHash;
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

        return stateHash == jumpStartHash || stateHash == jumpMiddleHash;
    }
}


