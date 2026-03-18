namespace Server;

public static class RoomManager
{
    private static readonly Dictionary<int, Room> _rooms = new();
    private static int _roomId = 1;

    public static Room CreateRoom()
    {
        var room = new Room(_roomId++);
        _rooms.Add(room.RoomId, room);
        return room;
    }

    public static Room GetRoom(int roomId)
    {
        return _rooms.TryGetValue(roomId, out var room) ? room : null;
    }

    public static List<int> GetEnableRooms()
    {
        List<int> rooms = new();

        foreach (var roomPair in _rooms)
        {
            if (roomPair.Value.IsEnable)
            {
                rooms.Add(roomPair.Key);
            }
        }

        return rooms;
    }

    public static void RemoveRoom(int roomId)
    {
        _rooms.Remove(roomId);
    }
}
