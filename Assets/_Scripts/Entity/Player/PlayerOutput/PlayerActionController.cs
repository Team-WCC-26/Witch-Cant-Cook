using System;
using UnityEngine;

public class PlayerActionController
{
    public static event Action<PlayerBrain> RemotePunchPlayed;
    private readonly PlayerBrain brain;

    private readonly PlayerAnimController animController;
    private readonly PlayerRagdollController ragdollController;
    private readonly PlayerMovement movement;

    private PlayerPhysicalMode prevMode;
    private bool awaitingRagdollState;

    public PlayerMovement Movement => movement;
    public bool CanRequestJump => movement.CanJump;
    public bool IsGroundedNow => movement.IsGroundedNow;
    public bool IsRagdollActive => ragdollController.IsActive;
    public float RagdollRecoveryProgress => ragdollController.RecoveryProgress;
    public bool IsRagdollLaunchPending => ragdollController.IsLaunchPending;
    public Quaternion PhysicsRot => ragdollController.PhysicsRot;
    public Vector3 PhysicsPos => ragdollController.PhysicsPos;
    public Vector3 VisualPos => ragdollController.VisualPos;

    private bool canAction = true;
    private float actionTime;
    private bool pendingPrimaryAction;
    private bool isLaunchJumpActive;
    private EntityCategory pendingPrimaryCategory;


    // Initialize movement, animation, and ragdoll controllers.
    public PlayerActionController(PlayerBrain brain)
    {
        this.brain = brain;

        animController = new PlayerAnimController(brain);
        ragdollController = new PlayerRagdollController(brain, animController);
        movement = new PlayerMovement(brain, brain.MoveSpeed, brain.RunMultiplier, brain.JumpPower, brain.Acceleration, brain.Deceleration);
        prevMode = PlayerPhysicalMode.Default;
    }

    public void ResetForRespawn()
    {
        ragdollController.Recover();
        prevMode = PlayerPhysicalMode.Default;
        awaitingRagdollState = false;
        pendingPrimaryAction = false;
        isLaunchJumpActive = false;
    }

    // Enter ragdoll and queue a launch after the initial collapse.
    public void LaunchRagdoll(Vector3 pos, Vector3 impulse)
    {
        if (brain.StateResolver is not LocalPlayerStateResolver resolver) return;
        resolver.EnterRagdoll();

        brain.Rocket?.StopFlight();
        brain.Interact.CancelPendingAttackImpact();
        brain.Interact.ForceDropHeld();
        ragdollController.Launch(pos, impulse);
        prevMode = PlayerPhysicalMode.Ragdoll;
        awaitingRagdollState = true;
    }

    // Follow the owner's pelvis while the limbs keep their local physics.
    public void UpdateRemoteRagdoll(Vector3 pos, Quaternion rot, Vector3 velocity)
    {
        if (prevMode != PlayerPhysicalMode.Ragdoll)
        {
            brain.Interact.CancelPendingAttackImpact();
            brain.Interact.ForceDropHeld();
            ragdollController.Enter();
            prevMode = PlayerPhysicalMode.Ragdoll;
        }
        ragdollController.UpdateRemoteTarget(pos, rot, velocity);
    }

    // Check recovery using the ragdoll body's own physics.
    public bool TickRagdollRecovery(bool paused, float deltaTime)
    {
        return ragdollController.TickRecovery(paused, deltaTime);
    }

    // Update actions and apply physical state transitions.
    public void UpdateTick(PlayerCombinedState state)
    {
        if (state.PhysicalMode == PlayerPhysicalMode.Ragdoll) awaitingRagdollState = false;
        brain.Rocket?.UpdateFlightState(state);
        if (!movement.IsEjectionActive) isLaunchJumpActive = false;
        UpdateActionState();
        movement.UpdateGroundState();

        // Update default locomotion and airborne animation parameters.
        if (!awaitingRagdollState && state.PhysicalMode == PlayerPhysicalMode.Default && !(brain.Rocket != null && brain.Rocket.IsFlying))
        {
            animController.UpdateTick(state, IsGroundedForAnimation(), IsFalling());
        }

        // Apply physical mode changes once per transition.
        if (state.PhysicalMode != prevMode && !awaitingRagdollState)
        {
            switch (state.PhysicalMode)
            {
                case PlayerPhysicalMode.Default when prevMode == PlayerPhysicalMode.Ragdoll:
                    ragdollController.Recover();
                    break;

                case PlayerPhysicalMode.Ragdoll:
                    brain.Interact.CancelPendingAttackImpact();
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

    // Advance the active physical movement controller.
    public void FixedTick(PlayerCombinedState state)
    {
        movement.UpdateForcedMovement(state.PhysicalMode == PlayerPhysicalMode.Default &&
            !ragdollController.IsActive && !(brain.Rocket != null && brain.Rocket.IsFlying));
        ragdollController.FixedTick();
        if (ragdollController.IsActive) return;
        if (brain.Rocket != null && brain.Rocket.FixedTick(state)) return;
        movement.UpdateEjectionMovement(state.PhysicalMode == PlayerPhysicalMode.Default);
        if (state.PhysicalMode == PlayerPhysicalMode.Default)
        {
            if (movement.IsForcedMovementActive)
            {
                movement.UpdateForcedMovementSpeed();
                if (!movement.IsEjectionActive)
                    movement.ApplyFallGravity();
                return;
            }

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
    // Play a punch when the action cooldown allows it.
    public bool PunchAction()
    {
        if (!canAction) return false;

        animController.PlayPrimaryAction();
        canAction = false;
        RecordPrimaryAction(EntityCategory.Player);
        return true;
    }

    // Play the held-item primary action when available.
    public bool TryEquipAction()
    {
        if (!canAction) return false;

        animController.PlayPrimaryAction();
        canAction = false;
        RecordPrimaryAction(brain.Interact.HeldObj.Category);
        return true;
    }

    private void RecordPrimaryAction(EntityCategory category)
    {
        if (brain.StateResolver is not LocalPlayerStateResolver) return;

        pendingPrimaryAction = true;
        pendingPrimaryCategory = category;
    }

    // Consume the pending action for the movement snapshot.
    public bool ConsumePrimaryAction(out EntityCategory category)
    {
        category = pendingPrimaryCategory;
        bool pending = pendingPrimaryAction;
        pendingPrimaryAction = false;
        return pending;
    }

    // Play a received primary action.
    public void PlayRemotePrimaryAction(PlayerCombinedState state)
    {
        if (state.PhysicalMode != PlayerPhysicalMode.Default) return;

        if (state.HeldEntityCategory == EntityCategory.Player)
            brain.Interact.QueueRemotePunchImpact();

        animController.UpdateTick(state, IsGroundedForAnimation(), IsFalling());
        animController.PlayPrimaryAction();
        if (state.HeldEntityCategory == EntityCategory.Player)
            RemotePunchPlayed?.Invoke(brain);
    }

    // Play the remote jump start animation.
    public void PlayRemoteJumpStart()
    {
        if (brain.StateResolver is not RemotePlayerStateResolver) return;
        animController.PlayJumpStart();
    }

    private void UpdateActionState()
    {
        bool isPlaying = animController.IsPrimaryActionPlaying();

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

    // Cancel the active action and reset its cooldown.
    public void CancelAction()
    {
        brain.Interact.CancelPendingAttackImpact();
        animController.CancelAction();
        canAction = true;
        actionTime = 0;
    }
    #endregion

    #region Jump Action
    // Applies one-shot jump requests after horizontal movement is resolved.
    private void ApplyJump(PlayerCombinedState state)
    {
        if (!state.JumpRequested || !movement.CanJump) return;
        movement.Jump();
        animController.PlayJumpAnim();
    }

    // Play the ordinary ejection jump animation.
    public void PlayLaunchJumpStart()
    {
        isLaunchJumpActive = true;
        animController.UpdateTick(brain.StateResolver.CurrentState, false, false);
        animController.PlayJumpStart();
    }

    private bool IsGroundedForAnimation()
    {
        if (isLaunchJumpActive && movement.IsEjectionActive) return false;
        return brain.StateResolver is RemotePlayerStateResolver remote
            ? remote.IsGroundedForAnimation
            : movement.IsGroundedNow;
    }

    private bool IsFalling()
    {
        if (brain.Col == null) return false;

        if (brain.StateResolver is RemotePlayerStateResolver remote)
        {
            if (remote.IsGroundedForAnimation) return false;
            // Ignore tiny interpolation corrections while the remote player is stationary.
            if (remote.VerticalSpeed >= -0.01f) return false;
        }
        else if (brain.Rb.linearVelocity.y >= 0f)
        {
            return false;
        }

        Bounds bounds = brain.Col.bounds;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
        float distance = bounds.extents.y - radius + brain.FallCheckDistance;

        return !Physics.SphereCast(
            bounds.center,
            radius,
            Vector3.down,
            out _,
            distance,
            brain.GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );
    }
    #endregion
}
