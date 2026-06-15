using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_InteractLobbyDoor)]
public partial class InteractLobbyDoorPacket
{
    public string PlayerId { get; set; }
}
