using UnityEngine;
using Protocol;
using System.Collections.Generic;
public class PlayerStateResolver
{
    protected readonly PlayerBrain brain;

    public PlayerCombinedState CurrentState { get; protected set; }

    public PlayerStateResolver(PlayerBrain brain)
    {
        this.brain = brain;

        CurrentState = new PlayerCombinedState(
            PlayerPhysicalMode.Default,
            Vector2.zero,
            false,
            PlayerInteraction.None,
            CatchableObjType.Default
        );
    }

    public virtual void UpdateTick()
    {
    }

    public virtual void FixedTick()
    {
    }

    public virtual void NotifyCollision(Collision collision)
    {
    }

    public virtual void ApplyRemotePacket(IReadOnlyList<PlayerMovementPacket> list)
    {
    }

    protected void SetCurrentState(PlayerCombinedState state)
    {
        CurrentState = state;
    }
}
