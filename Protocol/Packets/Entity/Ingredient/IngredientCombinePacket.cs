using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_IngredientCombine)]
public partial class IngredientCombinePacket
{
    public long SubjectEntityId { get; set; } // 조합 결과의 위치는 얘를 기준으로 잡음
    public long TargetEntityId { get; set; }
    public long NewEntityId { get; set; } // 조합 후 새 id가 부여되는 것이기에 기존에 존재하는 재료인 subject와 target에 해당하는 entity는 제거되어야 함
    public int ResultIngredientId { get; set; }
}
