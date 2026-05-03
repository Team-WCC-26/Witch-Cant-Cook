using UnityEngine;

public class PlayerPhysicalFSM
{
    private readonly PlayerBrain brain;

    private readonly float ragdollDuration = 2.5f;
    private readonly float recoverDuration = 0.7f;

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
                if (Time.time >= modeStartTime + ragdollDuration)
                {
                    SetMode(PlayerPhysicalMode.Recover);
                }
                break;

            case PlayerPhysicalMode.Recover:
                if (Time.time >= modeStartTime + recoverDuration)
                {
                    SetMode(PlayerPhysicalMode.Default);
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