using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_Notification)]
public partial class RoomNotificationPacket
{
    public string Message { get; set; }
}
