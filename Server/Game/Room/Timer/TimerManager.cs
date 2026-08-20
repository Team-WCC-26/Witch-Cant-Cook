namespace Server;

public sealed class TimerManager
{
    private long _nextId = 1;

    private readonly Dictionary<long, TimerBase> _timers = new();

    private readonly PriorityQueue<TimerBase, long> _queue = new();

    public TimerHandle Schedule<T>(
        long delayMs,
        T state,
        Action<T> callback)
    {
        long now = TimeUtil.NowMs();

        var timer = new Timer<T>
        {
            Id = _nextId++,
            DelayMs = delayMs,
            EndTime = now + delayMs,
            State = state,
            Callback = callback
        };

        _timers.Add(timer.Id, timer);
        _queue.Enqueue(timer, timer.EndTime);

        return new TimerHandle(timer.Id);
    }

    public void Tick(long deltaTime)
    {
        long now = TimeUtil.NowMs();

        while (_queue.TryPeek(out var timer, out long endTime))
        {
            if (endTime > now) break;

            _queue.Dequeue();

            if (endTime != timer.EndTime) continue;
            if (!_timers.TryGetValue(timer.Id, out var current)) continue;
            if (!ReferenceEquals(timer, current)) continue;
            if (timer.Paused || timer.Cancelled) continue;

            _timers.Remove(timer.Id);

            timer.Invoke();
        }
    }

    public bool Pause(TimerHandle handle)
    {
        if (!_timers.TryGetValue(handle.Id, out var timer)) return false;
        if (timer.Paused) return false;

        timer.RemainingTime = timer.EndTime - TimeUtil.NowMs();
        timer.Paused = true;

        return true;
    }

    public bool Resume(TimerHandle handle)
    {
        if (!_timers.TryGetValue(handle.Id, out var timer)) return false;
        if (!timer.Paused) return false;

        timer.Paused = false;
        timer.EndTime = TimeUtil.NowMs() + timer.RemainingTime;

        _queue.Enqueue(timer, timer.EndTime);

        return true;
    }

    public bool Cancel(TimerHandle handle)
    {
        if (!_timers.TryGetValue(handle.Id, out var timer)) return false;

        timer.Cancelled = true;
        _timers.Remove(handle.Id);

        return true;
    }

    public long RemainingTime(TimerHandle handle)
    {
        if (!_timers.TryGetValue(handle.Id, out var timer)) return 0;
        if (timer.Paused) return timer.RemainingTime;

        return Math.Max(0, timer.EndTime - TimeUtil.NowMs());
    }

    //public bool ResetDelayMs(TimerHandle handle, long delayMs)
    //{
    //    if (delayMs == 0) return false;
    //    if (!_timers.TryGetValue(handle.Id, out var timer)) return false;

    //    if (timer.Paused)
    //    {
    //        timer.DelayMs += delayMs - timer.RemainingTime;
    //        timer.RemainingTime = delayMs;
    //    }
    //    else
    //    {
    //        var newEndTime = TimeUtil.NowMs() + delayMs;
    //        timer.DelayMs += newEndTime - timer.EndTime;
    //        timer.EndTime = newEndTime;
    //    }

    //    return true;
    //}

    /// <summary>
    /// pot 전용
    /// </summary>
    public bool ResetDelayMs(TimerHandle handle, long delayMs)
    {
        if (delayMs == 0) return false;
        if (!_timers.TryGetValue(handle.Id, out var timer)) return false;

        timer.EndTime += delayMs - timer.DelayMs;
        timer.DelayMs = delayMs;

        _queue.Enqueue(timer, timer.EndTime);

        return true;
    }
}
