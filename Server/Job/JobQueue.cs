namespace Server;

// 룸별 작업 스테이징 큐
// - 작업은 FIFO로 쌓이며, 항상 하나의 Process 펌프만 "현재 샤드"를 향해 동작한다.
// - 샤드 마이그레이션 시 펌프가 스스로 목적지를 재조준(전달)하므로 작업 유실/중복 실행이 없다.
// - 샤드 배정 전(룸 등록 경합 창)에 Push된 작업은 임시 스테이징되고, InitShard에서 펌프가 장전되면 처리된다.
public class JobQueue
{
    private readonly Room _room;

    private readonly Queue<Action> _jobs = new();
    private int _maxProcessCnt = 100;

    private bool _isProcessing = false;

    // 현재 executor 샤드. 반드시 lock(_jobs) 내부에서만 읽기/교체한다.
    private Shard? _shard;

    public JobQueue(Room room)
    {
        _room = room;
    }

    // 현재 executor 샤드 (lock 내부에서 읽음)
    public Shard? CurrentShard
    {
        get
        {
            lock (_jobs)
            {
                return _shard;
            }
        }
    }

    // 초기 샤드 지정 (룸 등록 시 호출)
    // 등록 전에 Push되어 임시 스테이징된 작업이 있으면 여기서 펌프를 장전해 처리를 시작한다
    public void InitShard(Shard shard)
    {
        bool armPump = false;

        lock (_jobs)
        {
            _shard = shard;

            if (_jobs.Count > 0 && !_isProcessing)
            {
                _isProcessing = true;
                armPump = true;
            }
        }

        if (armPump)
        {
            Shard executor = shard;

            executor.Push(() => Process(executor));
        }
    }

    // 마이그레이션 시작: 이 순간부터 새 작업은 target 샤드를 executor로 사용한다
    public void BeginMigration(Shard target)
    {
        lock (_jobs)
        {
            _shard = target;
        }
    }

    public void Push(Action job)
    {
        Shard executor;

        lock (_jobs)
        {
            _jobs.Enqueue(job);

            if (_isProcessing) return;

            // 샤드가 아직 배정되지 않았으면(룸 등록 전 경합 창) 펌프를 장전하지 않고 스테이징만 한다
            // - InitShard에서 펌프를 장전하므로 작업이 유실되거나 펌프가 고립되지 않는다
            if (_shard == null) return;

            _isProcessing = true;
            executor = _shard;
        }

        executor.Push(() => Process(executor));
    }

    public void Clear()
    {
        lock (_jobs)
        {
            _jobs.Clear();
        }
    }

    // 샤드 스레드에서 실행되는 펌프. 스테이징 큐의 작업을 최대 _maxProcessCnt개 처리한다.
    private void Process(Shard executor)
    {
        int processCnt = 0;

        while (processCnt++ < _maxProcessCnt)
        {
            Action? job;
            Shard? forwardTarget;

            lock (_jobs)
            {
                if (!ReferenceEquals(_shard, executor))
                {
                    // 펌프가 도착한 샤드가 더 이상 소유 샤드가 아님(마이그레이션됨) - 실행 없이 전달
                    forwardTarget = _shard;
                    job = null;
                }
                else if (_jobs.Count == 0)
                {
                    _isProcessing = false;
                    return;
                }
                else
                {
                    forwardTarget = null;
                    job = _jobs.Dequeue();
                }
            }

            if (job == null)
            {
                // 전달: _isProcessing을 유지한 채 펌프가 새 목적지 샤드로 이어받는다
                forwardTarget!.Push(() => Process(forwardTarget));
                return;
            }

            long startUs = TimeUtil.NowUs();

            try
            {
                job.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            // 룸 부하 측정 (작업 단위)
            _room.RecordJobExecuted(TimeUtil.NowUs() - startUs);
        }

        // _maxProcessCnt 소진: 남은 작업이 잠기지 않도록 현재 소유 샤드에서 계속 처리하도록 재펌프
        Shard current;

        lock (_jobs)
        {
            current = _shard!;
        }

        current.Push(() => Process(current));
    }
}
