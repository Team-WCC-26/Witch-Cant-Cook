using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_GetRoom)]
[PacketId(PacketId.S_GetRoom)]
public partial class GetRoomPacket
{
    public List<int> RoomIds { get; set; } = new();
}
