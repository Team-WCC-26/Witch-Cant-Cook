using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_StageSet)]
[PacketId(PacketId.S_StageSet)]
public partial class StageSetPacket
{
    public int StageNum { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_StageStart)]
[PacketId(PacketId.S_StageStart)]
public partial class StageStartPacket
{
    public int StageNum { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_StageStop)]
[PacketId(PacketId.S_StageStop)]
public partial class StageStopPacket
{
    public int PendingMoney { get; set; } // 획득 예정량 (스테이지 동안 모인 임시 보유량)
    public int Money { get; set; } // 최종 보유량 (정산 후)
    public bool IsSuccess { get; set; } // 스테이지 성공 여부
}
