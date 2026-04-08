using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_CreateRoom)]
[PacketId(PacketId.S_CreateRoom)]
public partial class CreateRoomPacket
{
    public string RoomName { get; set; }
    public string RoomPassword { get; set; }
}
