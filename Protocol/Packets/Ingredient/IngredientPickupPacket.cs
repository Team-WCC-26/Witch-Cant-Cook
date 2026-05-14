
using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_IngredientPickup)]
public partial class IngredientPickupPacket
{
    public long EntityId { get; set; }
    public string PlayerID { get; set; }
}
