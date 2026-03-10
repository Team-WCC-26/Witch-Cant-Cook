using MemoryPack;

namespace Protocol
{
    [MemoryPackable]
    public partial class ChatMessagePacket : IPacket
    {
        public string Sender { get; set; }
        public string Message { get; set; }
    }
}
