using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityCombine)]
public partial class EntityCombinePacket
{
    // 손에 들고 있는 개체
    public long SubjectEntityId { get; set; }

    // 실제 상호작용 하는 개체
    public long TargetEntityId { get; set; }
}
