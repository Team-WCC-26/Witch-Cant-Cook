using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityInteract)]
public partial class EntityInteractPacket
{
    public string PlayerId { get; set; }
    public long TargetEntityId { get; set; }
}
