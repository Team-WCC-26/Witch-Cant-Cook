using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server;

public class JobQueue
{
    private readonly Queue<Action> _jobs = new();
    private bool _processing = false;

    public void Push(Action job)
    {
        lock (_jobs)
        {
            _jobs.Enqueue(job);

            if (_processing) return;

            _processing = true;
        }

        Process();
    }

    private void Process()
    {
        while (true)
        {
            Action job;

            lock (_jobs)
            {
                if (_jobs.Count == 0)
                {
                    _processing = false;
                    return;
                }

                job = _jobs.Dequeue();
            }

            job.Invoke();
        }
    }
}
