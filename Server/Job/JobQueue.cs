namespace Server;

public class JobQueue
{
    private readonly Queue<Action> _jobs = new();
    private Shard _shard;

    private int _maxProcessCnt = 100;
    private bool _isProcessing = false;
    public bool IsProcessing => _isProcessing;

    public void InitShard(Shard shard)
    {
        _shard = shard;
    }

    public void Push(Action job)
    {
        lock (_jobs)
        {
            _jobs.Enqueue(job);

            if (_isProcessing) return;

            _isProcessing = true;
        }

        _shard.Push(Process);
    }

    private void Process()
    {
        int processCnt = 0;

        while (processCnt++ < _maxProcessCnt)
        {
            Action job;

            lock (_jobs)
            {
                if (_jobs.Count == 0)
                {
                    _isProcessing = false;
                    return;
                }

                job = _jobs.Dequeue();
            }

            job.Invoke();
        }
    }
}
