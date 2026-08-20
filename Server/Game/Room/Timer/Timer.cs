namespace Server;

internal abstract class TimerBase
{
    public long Id;
    public long DelayMs;
    public long EndTime;

    public long RemainingTime;

    public bool Paused;
    public bool Cancelled;

    public abstract void Invoke();
}

internal sealed class Timer<T> : TimerBase
{
    public T State = default!;

    public Action<T> Callback = default!;

    public override void Invoke()
    {
        Callback?.Invoke(State);
    }
}
