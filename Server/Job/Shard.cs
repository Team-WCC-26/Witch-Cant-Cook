using System.Collections.Concurrent;

namespace Server;

public class Shard
{
    private readonly ConcurrentDictionary<string, Room> _roomDict = new(); // Hash로 바꾸고 id값만 저장하는게 나을수도
    private readonly ConcurrentQueue<Action> _jobs = new();
    private readonly SemaphoreSlim _signal = new(0);

    private int _roomCnt = 0;
    public int RoomCnt => _roomCnt;

    public void RegisterRoom(Room room)
    {
        room.InitShard(this);

        Push(() =>
        {
            _roomDict.TryAdd(room.Id, room);
            _roomCnt++;
        });
    }

    public void UnregisterRoom(string id)
    {
        Push(() =>
        {
            _roomDict.Remove(id, out var _);
            _roomCnt--;
        });
    }

    public void UnregisterRoom(Room room) => UnregisterRoom(room.Id);

    public void Push(Action job)
    {
        _jobs.Enqueue(job);
        _signal.Release();
    }

    public async Task StartProcess()
    {
        while (true)
        {
            await _signal.WaitAsync();

            if (_jobs.TryDequeue(out var job))
            {
                job.Invoke();
            }
        }
    }
}
