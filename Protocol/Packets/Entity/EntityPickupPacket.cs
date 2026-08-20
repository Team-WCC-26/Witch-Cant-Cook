using MemoryPack;

namespace Protocol;

[MemoryPackable]
public partial class EntityPickupPacket
{
    public long EntityId { get; set; }
    public string PlayerID { get; set; }
}
