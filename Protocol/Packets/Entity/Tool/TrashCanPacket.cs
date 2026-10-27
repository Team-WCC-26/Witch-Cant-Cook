using MemoryPack;

namespace Protocol;

// 쓰레기통 배출 (내부의 제거 불가능한 Tool들과 플레이어를 함께 내보냄)
[MemoryPackable]
[PacketId(PacketId.S_TrashCanEject)]
public partial class TrashCanEjectPacket
{
    public long TrashCanEntityId { get; set; }
    public int EjectSeed { get; set; }
    public long EjectDelayMs { get; set; }
    public List<string> PlayerIds { get; set; } = new();
    public List<long> EntityIds { get; set; } = new();
}
