using Protocol;

namespace Server;

public class SystemHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_Ping)]
    public static void PingPong(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<PingPongPacket>(package.Body);

        session.Player.LastPingTime = TimeUtil.NowMs();

        session.Player.Send(PacketSerializer.Serialize(packet, true));
    }

    [PacketHandler(PacketId.C_PingResult)]
    public static void GetPing(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<PingResultPacket>(package.Body);

        session.Player.Ping = packet.Ping;
    }
}
