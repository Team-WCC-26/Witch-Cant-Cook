using MemoryPack;

namespace Protocol;

/// <summary>
/// RoomId는 1번부터 시작
/// 0은 방이 가득 찬 경우
/// -1은 방이 존재하지 않음
/// </summary>
[MemoryPackable]
[PacketId(PacketId.C_JoinRoom)]
[PacketId(PacketId.S_JoinRoom)]
public partial class JoinRoomPacket : IPacket
{
    public int RoomId { get; set; }
}
