using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_RoomMoney)]
public partial class RoomMoneyPacket
{
    public int Money { get; set; }
}
