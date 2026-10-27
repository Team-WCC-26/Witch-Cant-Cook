using MemoryPack;

namespace Protocol;

// 요리제출구 배출 (제출 후 빈 접시 등 내용물을 내보냄)
[MemoryPackable]
[PacketId(PacketId.S_CookingSubmitEject)]
public partial class CookingSubmitEjectPacket
{
    public long CookingSubmitEntityId { get; set; }
    public int EjectSeed { get; set; }
    public long EjectDelayMs { get; set; }
    public List<long> EntityIds { get; set; } = new();
}
