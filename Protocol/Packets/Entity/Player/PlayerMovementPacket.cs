using MemoryPack;
using System.Numerics;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_PlayerMove)]
public partial class PlayerMovementPacket
{
    public string? PlayerId { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public PlayerCombinedState CombinedState { get; set; }
}

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

public enum CatchableObjType
{
    Default,
    Ingredient,
    Pan,
    Knife,
    Plate,
    Broom,
    Bucket
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