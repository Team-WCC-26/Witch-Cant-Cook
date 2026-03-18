using MemoryPack;

namespace Protocol;

[PacketId(PacketId.C_ChatMessage)]
[MemoryPackable]
public partial class ChatMessagePacket : IPacket
{
    public string Sender { get; set; }
    public string Message { get; set; }
}
