
using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityPickup)]
[PacketId(PacketId.S_EntityPickup)]
public partial class EntityPickupPacket
{
    public long EntityId { get; set; }
    public string PlayerID { get; set; }
}
