using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_ToolRegister)]
[PacketId(PacketId.S_ToolRegister)]
public partial class IngredientPutPacket
{
    public long IngredientId { get; set; }
    public long CountertopId { get; set; }
}
