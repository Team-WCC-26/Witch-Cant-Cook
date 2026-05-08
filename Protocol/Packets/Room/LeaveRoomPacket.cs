using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_LeaveRoom)]
public partial class LeaveRoomPacket
{
}
