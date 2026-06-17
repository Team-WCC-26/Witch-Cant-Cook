using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_EntityCombine)]
public partial class EntityCombineResultPacket
{
    public long FoodEntityId { get; set; }
    public long RemovedEntityId { get; set; }
    public IngredientStateData[] Ingredients { get; set; }
}

[MemoryPackable]
public partial struct IngredientStateData
{
    public int Id { get; set; }
    public IngredientState StateFlag { get; set; }
}
