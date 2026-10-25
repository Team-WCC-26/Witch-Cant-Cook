using MemoryPack;

namespace Protocol;

// 쓰레기통 배출 (제거 불가능한 Tool들을 내보냄, 플레이어는 카운트하지 않음)
[MemoryPackable]
[PacketId(PacketId.S_TrashCanEject)]
public partial class TrashCanEjectPacket
{
    public long TrashCanEntityId { get; set; }
    public int EjectSeed { get; set; }
    public long EjectDelayMs { get; set; }
    public List<long> EntityIds { get; set; } = new();
}
