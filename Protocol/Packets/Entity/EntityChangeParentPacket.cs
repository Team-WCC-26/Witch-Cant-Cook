using MemoryPack;

namespace Protocol;

[MemoryPackable]
public partial class EntityChangeParentPacket
{
    public long EntityId { get; set; }
    public long ParentEntityId { get; set; } = 0;
}
