using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_EntityCombine)]
public partial class EntityCombineResultPacket
{
    public bool Success { get; set; }
    public long SubjectEntityId { get; set; }
    public long TargetEntityId { get; set; }
    public long RemainingEntityId { get; set; }
    public long RemovedEntityId { get; set; }
    public int ResultIngredientId { get; set; }
}
