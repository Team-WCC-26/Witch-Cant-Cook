using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_PlayerEnter)]
public partial class PlayerEnterPacket
{
    public string NewPlayerID { get; set; }
}
