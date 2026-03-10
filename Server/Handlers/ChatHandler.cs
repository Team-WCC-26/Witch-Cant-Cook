using Protocol;

namespace Server
{
    public class ChatHandler : PacketHandlerBase<ChatMessagePacket>
    {
        [PacketHandler(PacketId.ChatMessage)]
        public void Handle(Session session, ChatMessagePacket packet)
        {
            
        }
    }
}
