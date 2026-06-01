using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_IngredientCombine)]
[PacketId(PacketId.S_IngredientCombine)]
public partial class IngredientCombinePacket
{
    public int ResultId { get; set; }

    // 손에 들고 있는 재료
    public int SubjectIngredient { get; set; }

    // 실제 상호작용 하는 재료
    public int TargetIngredient { get; set; }
}
