using Protocol;

namespace Server;

public class PlayerHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_PlayerMove)]
    public static void UpdateMove(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<PlayerMovementPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            session.Player.Position = packet.Position;
            session.Player.Rotation = packet.Rotation;
            session.Player.State = packet.CombinedState;
        });
    }
}
