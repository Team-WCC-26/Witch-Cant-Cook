using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_RoomMoney)]
public partial class RoomMoneyPacket
{
    public int Money { get; set; }
}

/// <summary>
/// 스테이지 종료 전까지 정산되지 않는 임시 보유량(획득 예정량) 동기화
/// </summary>
[MemoryPackable]
[PacketId(PacketId.S_RoomPendingMoney)]
public partial class RoomPendingMoneyPacket
{
    public int Money { get; set; }
}
