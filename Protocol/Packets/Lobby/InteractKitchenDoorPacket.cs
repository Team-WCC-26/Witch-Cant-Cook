using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_InteractKitchenDoor)]
public partial class InteractKitchenDoorPacket
{
    public string PlayerId { get; set; }
}
