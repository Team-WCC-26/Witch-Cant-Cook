using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_ToolRegister)]
[PacketId(PacketId.S_ToolRegister)]
public partial class ToolRegisterPacket
{
    public long EntityId { get; set; }
    public int ToolId { get; set; }
}
