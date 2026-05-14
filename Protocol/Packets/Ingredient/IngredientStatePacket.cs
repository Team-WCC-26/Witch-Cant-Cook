using MemoryPack;
using System.Numerics;

namespace Protocol;

[MemoryPackable]
public partial class IngredientStatePacket
{
    public long EntityID { get; set; }
    public int CurrentHP { get; set; }
    public byte State { get; set; }
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

public enum IngredientMovementState
{
    Idle = 0,
    Held = 1,
    Physics = 2,
    Thrown = 3,
}
