using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityPut)]
[PacketId(PacketId.S_EntityPut)]
public partial class EntityPutPacket
{
    public long EntityId { get; set; }
    public long CountertopEntityId { get; set; }
}
