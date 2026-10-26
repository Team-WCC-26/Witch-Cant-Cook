using MemoryPack;

namespace Protocol;

// 플레이어 리스폰 (서버가 맵을 모르므로 위치는 클라의 고정 스폰포인트에 의존)
[MemoryPackable]
[PacketId(PacketId.S_PlayerRespawn)]
public partial class PlayerRespawnPacket
{
    public string PlayerID { get; set; }
}
