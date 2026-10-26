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
    // 내용 없음: 스테이지 시간 종료 알림용 (정산 결과는 StageResultPacket으로 별도 전송)
}

[MemoryPackable]
[PacketId(PacketId.S_StageResult)]
public partial class StageResultPacket
{
    public int PendingMoney { get; set; } // 획득 예정량 (스테이지 동안 모인 임시 보유량)
    public int Money { get; set; } // 최종 보유량 (정산 후)
    public bool IsSuccess { get; set; } // 스테이지 성공 여부
}

[MemoryPackable]
[PacketId(PacketId.C_StageResultConfirm)]
public partial class StageResultConfirmPacket
{
    // 내용 없음: 정산 결과 확인 버튼
}
