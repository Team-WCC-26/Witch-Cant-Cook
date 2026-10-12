using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityExit)]
public partial class EntityExitPacket
{
    public long EntityId { get; set; }
}
