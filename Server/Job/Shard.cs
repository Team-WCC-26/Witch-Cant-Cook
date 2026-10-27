using System.Collections.Concurrent;

namespace Server;

public class Shard
{
    private readonly ConcurrentDictionary<string, Room> _roomDict = new(); // Hash로 바꾸고 id값만 저장하는게 나을수도
    private readonly ConcurrentQueue<Action> _jobs = new();
    private readonly AutoResetEvent _wakeEvent = new(false);

    private int _roomCnt = 0;
    public int RoomCnt => _roomCnt;

    // 리밸런서가 룸 목록을 순회하기 위한 읽기 전용 뷰
    public IReadOnlyDictionary<string, Room> Rooms => _roomDict;

    // === 부하 측정 (샤드 스레드 단일 작성자, 리밸런서는 근사 읽기) ===
    public long TickSweepEmaUs => Volatile.Read(ref _tickSweepEmaUs);
    private long _tickSweepEmaUs = 0; // 틱 스윕(등록된 전체 룸 Tick) 비용 EMA (μs)

    public long TotalOverrunCnt => Volatile.Read(ref _totalOverrunCnt);
    private long _totalOverrunCnt = 0; // 누적 오버런 횟수 (스윕 비용이 임계값 초과)

    public int ConsecutiveOverrunCnt => Volatile.Read(ref _consecutiveOverrunCnt);
    private int _consecutiveOverrunCnt = 0; // 연속 오버런 횟수

    public long ProcessedJobCnt => Volatile.Read(ref _processedJobCnt);
    private long _processedJobCnt = 0; // 누적 처리 작업 수

    public void RegisterRoom(Room room)
    {
        room.InitShard(this);

        Push(() => TryAddRoom(room));
    }

    public void UnregisterRoom(string id)
    {
        Push(() => TryRemoveRoom(id));
    }

    public void UnregisterRoom(Room room) => UnregisterRoom(room.Id);

    public void Push(Action job)
    {
        _jobs.Enqueue(job);
        _wakeEvent.Set();
    }

    // 룸 등록 (샤드 스레드에서 실행). RegisterRoom과 마이그레이션 등록 작업이 함께 사용한다.
    // 소멸된 룸은 등록하지 않는다 (제거와의 경합 방지).
    public bool TryAddRoom(Room room)
    {
        if (room.IsDestroyed) return false;

        if (_roomDict.TryAdd(room.Id, room))
        {
            _roomCnt++;
            return true;
        }

        return false;
    }

    // 룸 제거 (샤드 스레드에서 실행). 멱등 - 이미 없으면 카운트를 감소하지 않는다.
    public bool TryRemoveRoom(string id)
    {
        if (_roomDict.TryRemove(id, out var _))
        {
            _roomCnt--;
            return true;
        }

        return false;
    }

    // 등록된 룸들의 틱 비용 EMA 합 (초기 배치용 부하 추정치, μs/tick)
    public long EstimateTickLoadUs()
    {
        long total = 0;

        foreach (var room in _roomDict.Values)
        {
            total += room.TickCostEmaUs;
        }

        return total;
    }

    // 리밸런서용 부하 스냅샷
    public ShardLoadSnapshot TakeLoadSnapshot()
    {
        return new ShardLoadSnapshot(
            Volatile.Read(ref _tickSweepEmaUs),
            Volatile.Read(ref _totalOverrunCnt),
            Volatile.Read(ref _consecutiveOverrunCnt),
            _jobs.Count,
            Volatile.Read(ref _processedJobCnt));
    }

    public void StartProcess()
    {
        const int TickMs = 50;
        // 한 루프에 소비할 최대 잡 수 (잡 실행 중 등록된 잡은 다음 루프로 넘어가 Tick 기아를 막는다)
        const int MaxDrainPerLoop = 100;

        long lastTickTime = TimeUtil.NowMs();
        long nextTickTime = lastTickTime + TickMs;

        while (true)
        {
            int drainedCnt = 0;

            while (drainedCnt < MaxDrainPerLoop && _jobs.TryDequeue(out var job))
            {
                drainedCnt++;

                try
                {
                    job();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            _processedJobCnt += drainedCnt;

            long now = TimeUtil.NowMs();

            while (now >= nextTickTime)
            {
                long deltaTime = now - lastTickTime;
                lastTickTime = now;

                // 틱 스윕(등록된 전체 룸 Tick) 비용 측정
                long sweepStartUs = TimeUtil.NowUs();

                foreach (var room in _roomDict.Values)
                {
                    long tickStartUs = TimeUtil.NowUs();

                    try
                    {
                        room.Tick(deltaTime);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }

                    room.RecordTickCost(TimeUtil.NowUs() - tickStartUs);
                }

                RecordTickSweep(TimeUtil.NowUs() - sweepStartUs);

                nextTickTime += TickMs;
                now = TimeUtil.NowMs();
            }

            // 남은 잡이 있으면 대기 없이 다음 루프로 (잡 소비와 Tick을 교차시킨다)
            if (!_jobs.IsEmpty) continue;

            int waitTime = (int)Math.Max(0, nextTickTime - now);

            _wakeEvent.WaitOne(waitTime);
        }
    }

    // 틱 스윕 비용 기록 (샤드 스레드 전용)
    private void RecordTickSweep(long costUs)
    {
        _tickSweepEmaUs += (long)((costUs - _tickSweepEmaUs) * ShardBalanceConfig.EmaAlpha);

        if (costUs > ShardBalanceConfig.SweepOverrunThresholdUs)
        {
            _totalOverrunCnt++;
            _consecutiveOverrunCnt++;
        }
        else
        {
            _consecutiveOverrunCnt = 0;
        }
    }
}

// 샤드 부하 스냅샷 (리밸런서가 읽는 읽기 전용 값 모음)
public readonly struct ShardLoadSnapshot
{
    public readonly long TickSweepEmaUs;       // 틱 스윕 비용 EMA (μs)
    public readonly long TotalOverrunCnt;      // 누적 오버런 횟수
    public readonly int ConsecutiveOverrunCnt; // 연속 오버런 횟수
    public readonly int QueueDepth;            // 작업 큐 깊이 (샘플 시점)
    public readonly long ProcessedJobCnt;      // 누적 처리 작업 수

    public ShardLoadSnapshot(long tickSweepEmaUs, long totalOverrunCnt, int consecutiveOverrunCnt, int queueDepth, long processedJobCnt)
    {
        TickSweepEmaUs = tickSweepEmaUs;
        TotalOverrunCnt = totalOverrunCnt;
        ConsecutiveOverrunCnt = consecutiveOverrunCnt;
        QueueDepth = queueDepth;
        ProcessedJobCnt = processedJobCnt;
    }
}
