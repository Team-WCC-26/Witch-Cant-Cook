using UnityEngine;

public class PlayerInputFSM
{
    private readonly PlayerBrain brain;

    private KeyInput pendingKeyInput = KeyInput.None;

    private readonly float interactionCooldown = 0.1f;
    private float lastInteractionTime = -999f;

    public PlayerInteraction CurrentInteraction { get; private set; }

    public PlayerInputFSM(PlayerBrain brain)
    {
        this.brain = brain;
        brain.Input.InputPerformed += OnInputPerformed;
    }

    public void UpdateTick()
    {
        CurrentInteraction = ResolveInteraction();
    }

    private void OnInputPerformed(KeyInput keyInput)
    {
        pendingKeyInput |= keyInput;
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

    private bool CanAcceptInteraction()
    {
        return Time.time >= lastInteractionTime + interactionCooldown;
    }

    private PlayerInteraction ResolvePendingInteraction()
    {
        //if ((pendingKeyInput & KeyInput.Primary) != 0)
        //{
        //    return brain.Interact.IsHolding
        //        ? PlayerInteraction.Drop
        //        : PlayerInteraction.Pick;
        //}

        //if ((pendingKeyInput & KeyInput.Secondary) != 0)
        //{
        //    return brain.Interact.IsHolding
        //        ? PlayerInteraction.Throw
        //        : PlayerInteraction.None;
        //}

        //if ((pendingKeyInput & KeyInput.Interact) != 0)
        //{
        //    return PlayerInteraction.Use;
        //}

        return PlayerInteraction.None;
    }
}