using UnityEngine;

public class PlayerInputFSM
{
    public Vector2 MoveDir { get; private set; }
    public bool IsRun { get; private set; }
    public PlayerInteraction CurrentInteraction { get; private set; }
    public bool CurrentJumpRequested { get; private set; }

    //ref
    private readonly PlayerBrain brain;
    private readonly PlayerInputHandler inputHandler;

    //key input buffer
    private KeyInput pendingKeyInput = KeyInput.None;
    private bool pendingJumpRequested = false;

    //move option
    private float minMoveDirSqrMagnitude = 0.0001f;

    //interaction option
    private readonly float interactionCooldown = 0.1f;
    private float lastInteractionTime = -999f;

    public PlayerInputFSM(PlayerBrain brain)
    {
        this.brain = brain;
        inputHandler = brain.Input;
        inputHandler.InputPerformed += OnInputPerformed;
    }

    public void UpdateTick()
    {
        MoveDir = ResolveMoveDir();
        IsRun = ResolveRun();
        CurrentInteraction = ResolveInteraction();
        CurrentJumpRequested = ResolveJumpRequested();
    }

    private void OnInputPerformed(KeyInput keyInput)
    {
        if (keyInput == KeyInput.Jump)
        {
            pendingJumpRequested = true;
            return;
        }

        if (pendingKeyInput == KeyInput.None)
        {
            pendingKeyInput = keyInput;
        }
    }

    private Vector2 ResolveMoveDir()
    {
        Vector2 moveDir = inputHandler.RawMoveDir;
        
        if (moveDir.sqrMagnitude <= minMoveDirSqrMagnitude)
        {
            return Vector2.zero;
        }

        return moveDir;
    }

    private bool ResolveRun()
    {
        if (MoveDir.sqrMagnitude <= minMoveDirSqrMagnitude)
        {
            return false;
        }

        return inputHandler.RawIsRunning;
    }

    private PlayerInteraction ResolveInteraction()
    {
        if (pendingKeyInput == KeyInput.None)
        {
            return PlayerInteraction.None;
        }

        if (!CanAcceptInteraction())
        {
            pendingKeyInput = KeyInput.None;
            return PlayerInteraction.None;
        }

        PlayerInteraction interaction = ResolvePendingInteraction();
        pendingKeyInput = KeyInput.None;

        if (interaction != PlayerInteraction.None)
        {
            lastInteractionTime = Time.time;
        }

        return interaction;
    }

    #region Interaction Resolution
    private bool CanAcceptInteraction()
    {
        return Time.time >= lastInteractionTime + interactionCooldown;
    }

    private PlayerInteraction ResolvePendingInteraction()
    {
        if (pendingKeyInput == KeyInput.Primary)
        {
            return brain.Interact.IsHolding
                ? PlayerInteraction.HeldPrimary
                : PlayerInteraction.DefaultPrimary;
        }

        if (pendingKeyInput == KeyInput.Secondary)
        {
            return PlayerInteraction.Secondary;
        }

        if (pendingKeyInput == KeyInput.Interact)
        {
            return PlayerInteraction.KeyInteract;
        }

        return PlayerInteraction.None;
    }
    #endregion

    #region Jump Resolution
    // Consumes jump as a one-frame movement request.
    private bool ResolveJumpRequested()
    {
        bool jumpRequested = pendingJumpRequested;
        pendingJumpRequested = false;
        return jumpRequested;
    }
    #endregion
}
