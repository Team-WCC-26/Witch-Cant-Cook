using System.Diagnostics;

public static class TimeUtil
{
    private static readonly Stopwatch _sw = Stopwatch.StartNew();

    // 스톱워치 틱 -> 마이크로초 환산 단위 (극단적으로 낮은 타이머 주파수 환경 대비 최소 1 보장)
    private static readonly long _ticksPerUs = Math.Max(1, Stopwatch.Frequency / 1000000);

    public static long NowMs() => _sw.ElapsedMilliseconds;

    // 마이크로초 해상도의 현재 시각 (스톱워치 기반 단조 증가, 누적 나눗셈이라 오버플로우 없음)
    public static long NowUs() => _sw.ElapsedTicks / _ticksPerUs;
}
