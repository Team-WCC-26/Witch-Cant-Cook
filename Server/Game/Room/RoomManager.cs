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

    public Room CreateRoom(string roomName, string roomPassword)
    {
        Room room;

        lock (_lock)
        {
            string id = CreateUniqueRoomId();
            room = new(id, roomName, roomPassword);
            _rooms.TryAdd(id, room);
        }

        room.Init();
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
        if (room == null) return;

        // 소멸된 룸에 묶인 Detached 세션 레코드의 토큰을 무효화한다 (레지스트리에 위임)
        ServerContext.Instance.SessionRegistry.InvalidateRoomRecords(roomId);

        // 소멸 플래그를 Stop() 전에 설정 (이후 실행되는 샤드 등록 job이 소멸된 룸을 등록하지 않도록)
        room.MarkDestroyed();
        room.Stop();
        room.GetShard().UnregisterRoom(roomId);
    }

    // 참가 롤백 전용 회수: 파괴되지 않았고 플레이어가 없는 방만 제거한다.
    // 플레이어가 남아 있으면 파괴를 보류한다 (이후 그 플레이어의 Leave → 인원 0 → RemoveRoom 경로로 회수된다)
    // 샤드 작업 안에서 호출해야 한다 (인원/파괴 여부는 룸 소유 샤드 기준으로 판정한다)
    public void RemoveRoomIfEmpty(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var room)) return;
        if (room.IsDestroyed || room.PlayerCnt != 0) return;

        RemoveRoom(roomId);
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
