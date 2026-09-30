using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_IngredientCollision)]
public partial class IngredientCollisionPacket
{
    public long EntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_IngredientCorn)]
public partial class IngredientCornPacket
{
    public long EntityId { get; set; }
    public int ExplosionSeed { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_IngredientSquid)]
public partial class IngredientSquidPacket
{
    public long EntityId { get; set; }
    public long BlindDurationMs { get; set; }
    public List<string> PlayerIds { get; set; } = new();
}

[MemoryPackable]
[PacketId(PacketId.S_IngredientActivation)]
public partial class IngredientActivationPacket
{
    public long EntityId { get; set; }
    public IngredientActivationState State { get; set; }
    public string? TargetPlayerId { get; set; }
    public long DurationMs { get; set; }
}

public enum IngredientActivationState : byte
{
    Active,
    Stunned,
    Reactivated,
    Retargeted
}
