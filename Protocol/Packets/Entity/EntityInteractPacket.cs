using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityInteract)]
public partial class EntityInteractPacket
{
    public long TargetEntityId { get; set; }
}
