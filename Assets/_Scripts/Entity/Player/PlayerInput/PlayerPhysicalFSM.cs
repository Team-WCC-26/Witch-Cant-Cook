using UnityEngine;

public class PlayerPhysicalFSM
{
    private readonly PlayerBrain brain;

    private const float RespawnStunMultiplier = 2f;
    private float recoveryElapsed;
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
                else if (!isRecoveryPaused && RoomHudSession.Instance.State.IsStageRunning &&
                    StageReturnFlow.Instance.State.Phase == StageReturnPhase.Playing && !StageReturnFlow.IsInputBlocked)
                {
                    recoveryElapsed += Time.fixedDeltaTime;
                    if (recoveryElapsed >= brain.RagdollStunDuration * RespawnStunMultiplier)
                    {
                        recoveryElapsed = 0f;
                        PlayerSpawnManager.Instance?.RespawnPlayer(brain.PlayerId);
                    }
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

        recoveryElapsed = 0f;
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
        recoveryElapsed = 0f;
        isRecoveryPaused = paused;
    }

    private void SetMode(PlayerPhysicalMode mode)
    {
        CurrentMode = mode;
    }
}
