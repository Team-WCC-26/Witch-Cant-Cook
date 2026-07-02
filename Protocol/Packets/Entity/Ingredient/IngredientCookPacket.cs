using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_CookStart)]
[PacketId(PacketId.S_CookStart)]
public partial class CookStartPacket
{
    public long EntityId { get; set; }
    public long ToolEntityId { get; set; }
    public IngredientState CookType { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_CookCancel)]
[PacketId(PacketId.S_CookCancel)]
public partial class CookCancelPacket
{
    public long ToolEntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_CookComplete)]
public partial class CookCompletePacket
{
    public long EntityId { get; set; }
    public int IngredientId { get; set; }
    public IngredientState CookType { get; set; }
}
