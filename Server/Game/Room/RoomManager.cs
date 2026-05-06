using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Server;

// TODO => RoomPooling System
public class RoomManager
{
    public ConcurrentDictionary<string, Room> Rooms => _rooms;
    private readonly ConcurrentDictionary<string, Room> _rooms = new();

    private readonly ShardManager _shardManager;

    private readonly object _lock = new();

    private string _idChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private int _idLength = 6;

    public RoomManager(ShardManager shardManager)
    {
        _shardManager = shardManager;
    }

    public void Loop()
    {
        while (true)
        {
            foreach (var room in Rooms.Values)
            {
                room.Tick();
            }

            Thread.Sleep(50);
        }
    }

    public Room CreateRoom(string roomName, string roomPassword)
    {
        Room room;

        lock (_lock)
        {
            string id = CreateUniqueRoomId();
            room = new(id, roomName, roomPassword);
            _rooms.TryAdd(id, room);
        }

        _shardManager.RegisterRoom(room);

        return room;
    }

    public Room? GetRoom(string roomId)
    {
        return _rooms.TryGetValue(roomId, out var room) ? room : null;
    }

    public List<Room> GetEnableRooms()
    {
        List<Room> rooms = new();

        foreach (var roomPair in _rooms)
        {
            if (roomPair.Value.IsEnable())
            {
                rooms.Add(roomPair.Value);
            }
        }

        return rooms;
    }

    public void RemoveRoom(string roomId)
    {
        _rooms.Remove(roomId, out var room);
        room?.GetShard().UnregisterRoom(roomId);
    }

    private string CreateUniqueRoomId()
    {
        string newId;
        int attempts = 0;

        do
        {
            newId = RandomNumberGenerator.GetString(_idChars, _idLength);

            if (++attempts > 1000) throw new Exception("Id 생성 실패 -> 서버 분리 필요");
        } while (_rooms.ContainsKey(newId));

        return newId;
    }
}
