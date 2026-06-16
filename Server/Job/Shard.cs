using System.Collections.Concurrent;

namespace Server;

public class Shard
{
    private readonly ConcurrentDictionary<string, Room> _roomDict = new(); // Hash로 바꾸고 id값만 저장하는게 나을수도
    private readonly ConcurrentQueue<Action> _jobs = new();

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
    }

    public async Task StartProcess()
    {
        const int TickMs = 50;

        long lastTickTime = TimeUtil.NowMs();
        long nextTick = lastTickTime + TickMs;

        while (true)
        {
            if (_jobs.TryDequeue(out var job))
            {
                job.Invoke();
            }

            long now = TimeUtil.NowMs();

            while (now >= nextTick)
            {
                long deltaTime = now - lastTickTime;
                lastTickTime = now;

                foreach (var room in _roomDict.Values)
                {
                    room.Tick(deltaTime);
                }

                nextTick += TickMs;
            }

            await Task.Delay(1); // cpu 사용량 줄여야 한다면 약간의 휴식을 취하는 방식으로 수정
        }
    }
}
