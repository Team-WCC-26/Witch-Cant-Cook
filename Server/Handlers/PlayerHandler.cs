using Protocol;

namespace Server;

public class PlayerHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_PlayerMove)]
    public static void UpdateMove(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<PlayerMovementPacket>(package.Body);

        session.Player.Pos = packet.Position;
        session.Player.Rot = packet.Rotation;
        session.Player.State = packet.CombinedState;
    }
}
