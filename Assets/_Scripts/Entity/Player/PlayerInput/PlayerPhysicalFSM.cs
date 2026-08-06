using UnityEngine;

public class PlayerPhysicalFSM
{
    private readonly PlayerBrain brain;

    private float modeStartTime = 0f;

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
                if (Time.time >= modeStartTime + brain.RagdollStunDuration)
                {
                    SetMode(PlayerPhysicalMode.Default);
                    brain.Health.Reset();
                }
                break;
        }
    }

    public void NotifyCollision(Collision collision)
    {
        if (CurrentMode != PlayerPhysicalMode.Default)
        {
            return;
        }

        if (!IsObstacleCollision(collision))
        {
            return;
        }

        EnterRagdoll();
    }

    public void EnterRagdoll()
    {
        if (CurrentMode != PlayerPhysicalMode.Default)
        {
            return;
        }

        SetMode(PlayerPhysicalMode.Ragdoll);
    }

    private bool IsObstacleCollision(Collision collision)
    {
        return collision.collider.CompareTag("Obstacle");
    }

    private void SetMode(PlayerPhysicalMode mode)
    {
        CurrentMode = mode;
        modeStartTime = Time.time;
    }
}
