using Cysharp.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    public class JobWorker
    {
        private ConcurrentQueue<Action> _jobQueue = new();
        private SemaphoreSlim _signal = new(0);

        public async UniTaskVoid StartProcess()
        {
            while (true)
            {
                await _signal.WaitAsync();

                if (_jobQueue.TryDequeue(out var job))
                {
                    job.Invoke();
                }
            }
        }

        public void Push(Action job)
        {
            _jobQueue.Enqueue(job);
            _signal.Release();
        }
    }
}
