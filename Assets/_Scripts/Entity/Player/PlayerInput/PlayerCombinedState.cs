using UnityEngine;

public enum PlayerPhysicalMode
{
    Default,
    Ragdoll,
    Recover
}

public enum PlayerInteraction
{
    None,
    Pick,
    Drop,
    Throw,
    Use,
    SpecialInteract
}

public readonly struct PlayerCombinedState
{
    public readonly PlayerPhysicalMode PhysicalMode;
    public readonly Vector2 MoveDir;
    public readonly bool IsRun;
    public readonly PlayerInteraction Interaction;

    
    public PlayerCombinedState(
        PlayerPhysicalMode physicalMode,
        Vector2 moveDir = default,
        bool isRun = false,
        PlayerInteraction interaction = PlayerInteraction.None)
    {
        PhysicalMode = physicalMode;
        MoveDir = moveDir;
        IsRun = isRun;
        Interaction = interaction;
    }
}