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

    [PacketHandler(PacketId.C_PlayerDamage)]
    public static void DamagePlayer(Session session, PacketPackageInfo package) // TODO => 본인이 맞았다고 판정된것만 적용할건지 정해야함
    {
        var packet = DeSerialize<PlayerDamagePacket>(package.Body);
        var room = session.Player.Room;

        if (packet.PlayerId != session.Player.PlayerId) return;

        room.PushJob(() =>
        {
            session.Player.ApplyDamage(Math.Min(packet.Damage, Player.MaxHp));
        });
    }
}
