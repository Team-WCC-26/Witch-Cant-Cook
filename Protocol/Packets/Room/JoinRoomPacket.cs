using MemoryPack;

namespace Protocol;

/// <summary>
/// RoomId는 6자리 문자열(숫자, 영대문자 조합)
/// <para/> RoomID가 F인 경우 방이 가득 참
/// <para/> RoomID가 N인 경우 방이 없음
/// </summary>
[MemoryPackable]
[PacketId(PacketId.C_JoinRoom)]
[PacketId(PacketId.S_JoinRoom)]
public partial class JoinRoomPacket
{
    public string RoomId { get; set; }
    public string RoomPassword { get; set; }
    public int PlayerCnt { get; set; }
}
