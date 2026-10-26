using UnityEngine;

public class PlayerPhysicalFSM
{
    private readonly PlayerBrain brain;

    private float modeStartTime = 0f;
    private bool isRecoveryPaused;

    public PlayerPhysicalMode CurrentMode { get; private set; }

    public PlayerPhysicalFSM(PlayerBrain brain)
    {
        this.brain = brain;
        CurrentMode = PlayerPhysicalMode.Default;
        modeStartTime = Time.time;
    }

    public void FixedTick()
    {
        switch (CurrentMode)
        {
            case PlayerPhysicalMode.Default:
                break;

            case PlayerPhysicalMode.Ragdoll:
                if (!isRecoveryPaused && Time.time >= modeStartTime + brain.RagdollStunDuration)
                {
                    SetMode(PlayerPhysicalMode.Default);
                    brain.Health.Reset();
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

        SetMode(PlayerPhysicalMode.Ragdoll);
    }

    public void SetRecoveryPaused(bool paused)
    {
        if (isRecoveryPaused == paused) return;
        isRecoveryPaused = paused;
        if (!paused && CurrentMode == PlayerPhysicalMode.Ragdoll)
            modeStartTime = Time.time;
    }

    private void SetMode(PlayerPhysicalMode mode)
    {
        CurrentMode = mode;
        modeStartTime = Time.time;
    }
}
