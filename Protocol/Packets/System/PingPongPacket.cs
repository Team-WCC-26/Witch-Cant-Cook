using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_Ping)]
[PacketId(PacketId.S_Pong)]
public partial class PingPongPacket
{
    public long TimeMs { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_PingResult)]
public partial class PingResultPacket
{
    public string PlayerId { get; set; }
    public float Ping { get; set; }
}