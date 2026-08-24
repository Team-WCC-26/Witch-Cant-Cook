using Protocol;

namespace Server;

public class GameLoopHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_StageSet)]
    public static void SetStage(Session session, PacketPackageInfo package)
    {
        var room = session.Player.Room;

        var packet = DeSerialize<StageSetPacket>(package.Body);

        room.PushJob(() =>
        {
            room.SetStage(packet.StageNum);

            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }
}
