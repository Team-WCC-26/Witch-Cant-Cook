using Cysharp.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Server
{
    public class JobWorker
    {
        private ConcurrentQueue<Action> _jobQueue = new();
        private SemaphoreSlim _signal;

        public async UniTaskVoid StartProcess(CancellationToken token)
        {
            while (true)
            {
                await _signal.WaitAsync(token);

                if (_jobQueue.TryDequeue(out var job))
                {
                    job.Invoke();
                }
            }
        }

        public void Initialize()
        {
            _jobQueue.Clear();

            _signal?.Dispose();
            _signal = new(0);
        }

        public void Push(Action job)
        {
            _jobQueue.Enqueue(job);
            _signal.Release();
        }
    }
}
