using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_CookStart)]
public partial class CookStartPacket
{
    public long ToolEntityId { get; set; }
    public long CookingTimeMs { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_CookPause)]
public partial class CookPausePacket
{
    public long ToolEntityId { get; set; }
}

[MemoryPackable]
public partial class CookProcessPacket
{
    public long EntityId { get; set; }
    public float Process { get; set; }
}

[MemoryPackable]
public partial class CookCompletePacket
{
    public long ToolEntityId { get; set; }
    public long IngredientEntityId { get; set; }
    public IngredientState CookType { get; set; }
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
