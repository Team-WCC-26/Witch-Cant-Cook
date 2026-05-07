using Protocol;

namespace Server;

public class RoomHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_CreateRoom)]
    public static void CreateRoom(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<CreateRoomPacket>(package.Body);

        var newRoom = ServerContext.Instance.RoomManager.CreateRoom(packet.RoomName, packet.RoomPassword);
        JoinRoom(session, newRoom);
    }

    [PacketHandler(PacketId.C_GetRoom)]
    public static void GetRoom(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<GetRoomPacket>(package.Body);
        var rooms = ServerContext.Instance.RoomManager.GetEnableRooms();

        packet.RoomDatas.Clear();

        foreach (var room in rooms)
        {
            packet.RoomDatas.Add(new RoomData()
            {
                Id = room.Id,
                Name = room.Name,
                BIsPrivate = !string.IsNullOrEmpty(room.Password)
            });
        }
        
        session.Player.Send(PacketSerializer.Serialize(packet, true));
    }

    [PacketHandler(PacketId.C_JoinRoom)]
    public static void JoinRoom(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<JoinRoomPacket>(package.Body);
        var room = ServerContext.Instance.RoomManager.GetRoom(packet.RoomId);

        if (room != null && (string.IsNullOrEmpty(room.Password) || room.Password == packet?.RoomPassword))
        {
            JoinRoom(session, room);
        }
    }

    public static void JoinRoom(Session session, Room? room)
    {
        if (room == null) return;

        room?.PushJob(() =>
        {
            if (!room.IsEnable())
            {

                return;
            }

            PlayerEnterPacket playerEnterPacket = new()
            {
                NewPlayerID = session.Player.PlayerId
            };

            var packet = PacketSerializer.Serialize(playerEnterPacket, true);

            foreach (var player in room.Players)
            {
                player.Send(packet);
            }

            room.Enter(session.Player);

            JoinRoomPacket joinRoomPacket = new()
            {
                RoomId = room.Id,
                PlayerCnt = room.PlayerCnt
            };

            session.Player.Send(PacketSerializer.Serialize(joinRoomPacket, true));
        });

        room?.Notificate($"{session.Player.PlayerId} joined this Room.");
    }
}
