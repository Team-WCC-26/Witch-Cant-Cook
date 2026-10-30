using UnityEngine;

public class PlayerPhysicalFSM
{
    private readonly PlayerBrain brain;

    private bool isRecoveryPaused;

    public PlayerPhysicalMode CurrentMode { get; private set; }

    public PlayerPhysicalFSM(PlayerBrain brain)
    {
        this.brain = brain;
        CurrentMode = PlayerPhysicalMode.Default;
    }

    public void FixedTick()
    {
        switch (CurrentMode)
        {
            case PlayerPhysicalMode.Default:
                break;

            case PlayerPhysicalMode.Ragdoll:
                if (brain.ActionController.TickRagdollRecovery(isRecoveryPaused, Time.fixedDeltaTime))
                {
                    SetMode(PlayerPhysicalMode.Default);
                }
                break;
        }
    }

    public void EnterRagdoll()
    {
        if (CurrentMode != PlayerPhysicalMode.Default)
        {
            return;
        }

        brain.Health.SetRagdollActive(true);
        SetMode(PlayerPhysicalMode.Ragdoll);
    }

    public void ResetForRespawn()
    {
        isRecoveryPaused = false;
        brain.Health.SetRagdollActive(false);
        SetMode(PlayerPhysicalMode.Default);
    }

    public void SetRecoveryPaused(bool paused)
    {
        if (isRecoveryPaused == paused) return;
        isRecoveryPaused = paused;
    }

    private void SetMode(PlayerPhysicalMode mode)
    {
        CurrentMode = mode;
    }
}
