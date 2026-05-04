using Protocol;
using System.Text;

namespace Server;

public class ChatHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_ChatMessage)]
    public static void Handle(Session session, PacketPackageInfo package)
    {
        if (session?.Player?.Room == null) return;

        var chatPacket = DeSerialize<ChatMessagePacket>(package.Body);
        chatPacket.Sender = session.SessionID;

        session.Player.Room.BroadCast(PacketSerializer.Serialize(chatPacket, true));
    }
}
