using System.Collections.Concurrent;

namespace Server;

public class Shard
{
    private readonly ConcurrentDictionary<string, Room> _roomDict = new(); // Hash로 바꾸고 id값만 저장하는게 나을수도
    private readonly ConcurrentQueue<Action> _jobs = new();
    private readonly AutoResetEvent _wakeEvent = new(false);

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
        _wakeEvent.Set();
    }

    public void StartProcess()
    {
        const int TickMs = 50;

        long lastTickTime = TimeUtil.NowMs();
        long nextTickTime = lastTickTime + TickMs;

        while (true)
        {
            while (_jobs.TryDequeue(out var job))
            {
                job();
            }

            long now = TimeUtil.NowMs();

            while (now >= nextTickTime)
            {
                long deltaTime = now - lastTickTime;
                lastTickTime = now;

                foreach (var room in _roomDict.Values)
                {
                    room.Tick(deltaTime);
                }

                nextTickTime += TickMs;
                now = TimeUtil.NowMs();
            }

            int waitTime = (int)Math.Max(0, nextTickTime - now);

            _wakeEvent.WaitOne(waitTime);
        }
    }
}
