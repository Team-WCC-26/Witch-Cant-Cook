using System.Collections.Concurrent;

namespace Server;

public class RoomManager
{
    private readonly ConcurrentDictionary<int, Room> _rooms = new();
    private int _roomId = 0;

    private readonly ShardManager _shardManager;

    private object _lock = new();

    public RoomManager(ShardManager shardManager)
    {
        _shardManager = shardManager;
    }

    public Room CreateRoom()
    {
        int id = Interlocked.Increment(ref _roomId);
        Room room = new(id);
        _rooms.TryAdd(id, room);
        _shardManager.RegisterRoom(room);

        return room;
    }

    public Room? GetRoom(int roomId)
    {
        return _rooms.TryGetValue(roomId, out var room) ? room : null;
    }

    public List<int> GetEnableRooms()
    {
        List<int> rooms = new();

        foreach (var roomPair in _rooms)
        {
            if (roomPair.Value.IsEnable())
            {
                rooms.Add(roomPair.Key);
            }
        }

        return rooms;
    }

    public void RemoveRoom(int roomId)
    {
        _rooms.Remove(roomId, out var room);
        room?.GetShard().UnregisterRoom(roomId);
    }
}
