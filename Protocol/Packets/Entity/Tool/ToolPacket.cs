using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_ToolPlayerEnter)]
[PacketId(PacketId.S_ToolPlayerEnter)]
public partial class ToolPlayerEnterPacket
{
    public long ToolEntityId { get; set; }
    public string PlayerId { get; set; }
}
