using System.Diagnostics;

public static class TimeUtil
{
    private static readonly Stopwatch _sw = Stopwatch.StartNew();

    public static long NowMs() => _sw.ElapsedMilliseconds;
}
