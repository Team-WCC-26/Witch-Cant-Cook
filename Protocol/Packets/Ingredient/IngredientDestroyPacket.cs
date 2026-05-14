using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_IngredientDestroy)]
[PacketId(PacketId.S_IngredientDestroy)]
public partial class IngredientDestroyPacket
{
    public long EntityId { get; set; }
}
