using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_IngredientCombine)]
public partial class IngredientCombinePacket
{
    // 손에 들고 있는 재료
    public long SubjectEntityId { get; set; }

    // 실제 상호작용 하는 재료
    public long TargetEntityId { get; set; }
}
