

using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_PlayerLeave)]
public partial class PlayerLeavePacket
{
    public string PlayerID { get; set; }
}
