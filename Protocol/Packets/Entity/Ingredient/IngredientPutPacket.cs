using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_IngredientPut)]
[PacketId(PacketId.S_IngredientPut)]
public partial class IngredientPutPacket
{
    public long IngredientId { get; set; }
    public long CountertopId { get; set; }
}
