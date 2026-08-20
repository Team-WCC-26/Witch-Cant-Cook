using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityInsert)]
public partial class EntityInsertPacket
{
    public long SubjectEntityId { get; set; }
    public long TargetEntityId { get; set; }
}
