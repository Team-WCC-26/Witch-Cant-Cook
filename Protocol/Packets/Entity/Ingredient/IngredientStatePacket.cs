using MemoryPack;
using System.Numerics;

namespace Protocol;

[MemoryPackable]
public partial class IngredientStatePacket
{
    public long EntityId { get; set; }
    public int CurrentHP { get; set; }
    public IngredientState State { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_CookStart)]
[PacketId(PacketId.S_CookStart)]
public partial class CookStartPacket
{
    public long EntityId { get; set; }
    public IngredientState CookType { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_CookCancel)]
[PacketId(PacketId.S_CookCancel)]
public partial class CookCancelPacket
{
    public long EntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_CookComplete)]
public partial class CookCompletePacket
{
    public long EntityId { get; set; }
    public int IngredientId { get; set; }
    public IngredientState CookType { get; set; }
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
    Grilled = 1 << 1,
    Boiled = 1 << 2,
    Roasted = 1 << 3
}

public enum IngredientMovementState
{
    Idle = 0,
    Held = 1,
    Physics = 2,
    Thrown = 3,
}
