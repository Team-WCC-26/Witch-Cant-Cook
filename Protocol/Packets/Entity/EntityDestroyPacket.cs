using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityDestroy)]
[PacketId(PacketId.S_EntityDestroy)]
public partial class EntityDestroyPacket
{
    public long EntityId { get; set; }
}
