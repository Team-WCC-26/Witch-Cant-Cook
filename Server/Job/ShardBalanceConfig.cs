namespace Server;

// 샤드 간 룸 재배치(리밸런싱) 정책 설정
// 실행 중에도 값을 바꾸면 다음 측정 주기부터 반영된다
public static class ShardBalanceConfig
{
    // 리밸런서 동작 여부
    public static bool Enabled { get; set; } = true;

    // 측정 지표 요약 로그 출력 여부 (주기당 1회)
    public static bool EnableMetricsLog { get; set; } = true;

    // 부하 측정 주기 (ms)
    public static int MeasurementPeriodMs { get; set; } = 1000;

    // 부하 편차 임계값 (편차 = (최대 - 최소) / 평균, 0.2 ~ 0.3 권장)
    public static double DeviationThreshold { get; set; } = 0.25;

    // 편차 지속 판정 주기 수 (측정 주기 1000ms 기준 4 => 약 4초 지속 시 이동 검토)
    public static int SustainSeconds { get; set; } = 4;

    // 전역 재배치 쿨다운 (ms) - 이동 후 이 시간 동안 추가 이동 금지
    public static int GlobalCooldownMs { get; set; } = 5000;

    // 개별 룸 재배치 쿨다운 (ms) - 같은 룸이 재이동되기까지의 최소 간격
    public static int RoomMoveCooldownMs { get; set; } = 30000;

    // 비용 EMA 가중치 (0 < alpha <= 1)
    public static double EmaAlpha { get; set; } = 0.3;

    // 한 번의 판정에서 이동시킬 최대 룸 수
    public static int MaxRoomsPerDecision { get; set; } = 1;

    // 틱 스윕 오버런 판정 기준 (μs) - 스윕 비용이 이 값을 초과하면 오버런 (틱 주기 50ms)
    public static long SweepOverrunThresholdUs { get; set; } = 50000;

    // 연속 오버런 트리거 횟수 - 도달하면 편차 지속 조건 없이 즉시 이동 검토
    public static int OverrunTriggerCnt { get; set; } = 5;

    // 재배치를 "개선"으로 인정할 최소 편차 감소 폭 (μs/tick) - 플래피킹 방지용
    public static long MinimumImprovementUs { get; set; } = 1000;

    // 초당 틱 수 (틱 주기 50ms 기준)
    public static int TicksPerSecond { get; set; } = 20;
}
