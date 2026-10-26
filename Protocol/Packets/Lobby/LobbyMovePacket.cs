using MemoryPack;

namespace Protocol;

// 정산 결과를 룸의 모든 플레이어가 확인하면 로비 이동을 지시한다 (실제 이동 연산은 클라이언트가 수행)
[MemoryPackable]
[PacketId(PacketId.S_LobbyMove)]
public partial class LobbyMovePacket
{
}
