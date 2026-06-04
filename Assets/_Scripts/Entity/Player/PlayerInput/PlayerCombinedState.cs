using MemoryPack;
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
    DefaultPrimary,
    HeldPrimary,
    Secondary,
    KeyInteract,
    SpecialInteract
}

[MemoryPackable]
public readonly partial struct PlayerCombinedState
{
    public readonly PlayerPhysicalMode PhysicalMode;
    public readonly Vector2 MoveDir;
    public readonly bool IsRun;
    public readonly PlayerInteraction Interaction;
    public readonly CatchableObjType HeldObjType;

    
    public PlayerCombinedState(
        PlayerPhysicalMode physicalMode,
        Vector2 moveDir = default,
        bool isRun = false,
        PlayerInteraction interaction = PlayerInteraction.None,
        CatchableObjType heldObjType = CatchableObjType.Default)
    {
        PhysicalMode = physicalMode;
        MoveDir = moveDir;
        IsRun = isRun;
        Interaction = interaction;
        HeldObjType = heldObjType;
    }
}