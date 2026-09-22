using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_PanStoveExit)]
[PacketId(PacketId.S_PanStoveExit)]
public partial class PanStoveExitPacket
{
    public long PanEntityId { get; set; }
}
