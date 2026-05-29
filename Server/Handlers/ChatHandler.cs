using Protocol;

namespace Server;

public class ChatHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_ChatMessage)]
    public static void Handle(Session session, PacketPackageInfo package)
    {
        var room = session?.Player?.Room;

        if (room == null) return;

        var chatPacket = DeSerialize<ChatMessagePacket>(package.Body);
        chatPacket.Sender = session.SessionID;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(chatPacket, true));
        });
    }
}
