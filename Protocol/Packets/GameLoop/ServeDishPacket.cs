using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_ServeDish)]
[PacketId(PacketId.S_ServeDish)]
public partial class ServeDishPacket
{
    public long EntityId { get; set; }
    public bool Success { get; set; }
}
