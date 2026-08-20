namespace Server;

public readonly struct TimerHandle
{
    internal readonly long Id;

    internal TimerHandle(long id)
    {
        Id = id;
    }
}
