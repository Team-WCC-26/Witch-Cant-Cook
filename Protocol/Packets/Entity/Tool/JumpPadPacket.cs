using MemoryPack;

namespace Protocol;

/// <summary>
/// 점프대 이벤트 종류
/// </summary>
public enum JumpPadEventType
{
    Fire,   // 장전 완료 발사
    Cancel, // 장전 취소
    Return, // 복귀 완료
}

[MemoryPackable]
[PacketId(PacketId.C_JumpPadPlayerEnter)]
public partial class JumpPadPlayerEnterPacket
{
    public long PadEntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_JumpPadPlayerExit)]
public partial class JumpPadPlayerExitPacket
{
    public long PadEntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_JumpPadEvent)]
public partial class JumpPadEventPacket
{
    public long PadEntityId { get; set; }
    public JumpPadEventType EventType { get; set; }
    public int FireRound { get; set; }
}
