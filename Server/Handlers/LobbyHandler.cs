using Protocol;

namespace Server;

public class LobbyHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_InteractDoor)]
    public static void InteractDoor(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<InteractDoorPacket>(package.Body);

        session.Player.PushGameplayJob(session, room =>
        {
            room.InteractDoor(packet.DoorId, session.Player.PlayerId);
        });
    }

    [PacketHandler(PacketId.C_StopInteractDoor)]
    public static void StopInteractDoor(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<InteractDoorPacket>(package.Body);

        session.Player.PushGameplayJob(session, room =>
        {
            room.StopInteractDoor(packet.DoorId, session.Player.PlayerId);
        });
    }
}
