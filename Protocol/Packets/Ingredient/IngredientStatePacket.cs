using MemoryPack;
using System.Numerics;

namespace Protocol;

[MemoryPackable]
public partial class IngredientStatePacket
{
    public long EntityID { get; set; }
    public int CurrentHP { get; set; }
    public IngredientState State { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_IngredientCut)]
public partial class CutIngredientPacket
{
    public long EntityID { get; set; }
    public string PlayerID { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_IngredientGrill)]
public partial class GrillIngredientPacket
{
    public long EntityID { get; set; }
    public string PlayerID { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_IngredientBoil)]
public partial class BoilIngredientPacket
{
    public long EntityID { get; set; }
    public string PlayerID { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_IngredientRoast)]
public partial class RoastIngredientPacket
{
    public long EntityID { get; set; }
    public string PlayerID { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_IngredientCancelGrill)]
public partial class CancelGrillIngredientPacket
{
    public long EntityID { get; set; }
}

[MemoryPackable]
public partial class IngredientMovementStatePacket
{
    public long EntityId { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Velocity { get; set; }
    public IngredientMovementState State { get; set; }
}

[Flags]
public enum IngredientState : byte
{
    None = 0,
    Cut = 1 << 0,
    Fried = 1 << 1,
    Boiled = 1 << 2,
    Burned = 1 << 3
}

public enum IngredientMovementState
{
    Idle = 0,
    Held = 1,
    Physics = 2,
    Thrown = 3,
}
