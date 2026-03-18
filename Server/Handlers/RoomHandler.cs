using Protocol;

namespace Server;

public class RoomHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_CreateRoom)]
    public static void CreateRoom(Session session, PacketPackageInfo package)
    {
        JoinRoom(session, RoomManager.CreateRoom());
    }

    [PacketHandler(PacketId.C_GetRoom)]
    public static void GetRoom(Session session, PacketPackageInfo package)
    {
        GetRoomPacket packet = new();
        packet.RoomIds = RoomManager.GetEnableRooms();
        
        session.Player.Send(PacketSerializer.Serialize(packet, true));
    }

    [PacketHandler(PacketId.C_JoinRoom)]
    public static void JoinRoom(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<JoinRoomPacket>(package.Body);

        JoinRoom(session, RoomManager.GetRoom(packet.RoomId));
    }

    public static void JoinRoom(Session session, Room room)
    {
        if (room == null) return;

        room.Enter(session.Player);
        
        JoinRoomPacket packet = new();
        packet.RoomId = room.RoomId;

        session.Player.Send(PacketSerializer.Serialize(packet, true));

        room.Notificate($"{session.Player.PlayerId} joined this Room.");
    }
}
