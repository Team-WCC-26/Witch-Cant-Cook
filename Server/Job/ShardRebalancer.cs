using System.Text;

namespace Server;

// 주기적으로 샤드 부하를 측정하고, 부하 편차가 지속되면 룸 하나를 다른 샤드로 재배치한다.
// TimerManager가 아닌 전용 Task 루프 + Sleep 사용 (샤드 StartProcess와 같은 패턴).
public class ShardRebalancer
{
    private readonly ShardManager _shardManager;

    // 직전 주기에 읽은 룸별 누적 작업 수 (델타 소비용 마지막 읽기값)
    // 매 주기 관측된 룸만 남기고 재구축되므로 readonly 아님
    private Dictionary<string, long> _lastJobCntById = new();

    // 직전 주기의 샤드별 스냅샷 (지표 로그의 증가분 계산용)
    private ShardLoadSnapshot[] _prevSnapshots = Array.Empty<ShardLoadSnapshot>();

    private long _lastMoveAtMs = 0; // 마지막 재배치 시각 (전역 쿨다운용)
    private int _sustainCnt = 0;    // 편차 지속 주기 카운트 (히스테리시스)

    public ShardRebalancer(ShardManager shardManager)
    {
        _shardManager = shardManager;
    }

    public void RunLoop()
    {
        while (true)
        {
            Thread.Sleep(ShardBalanceConfig.MeasurementPeriodMs);

            // 리밸런서가 비활성화된 주기는 판정/이동 없이 대기만 한다 (실행 중 변경 반영)
            if (!ShardBalanceConfig.Enabled) continue;

            try
            {
                Check();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }

    private void Check()
    {
        long now = TimeUtil.NowMs();

        IReadOnlyList<Shard> shards = _shardManager.Shards;

        if (shards.Count <= 1) return;

        // 1) 샤드별 부하 추정치 스냅샷
        //    룸 부하 = 틱 비용 EMA + (주기 내 작업 수 x 작업 비용 EMA / 초당 틱 수), μs/tick 환산
        long[] loadByShard = new long[shards.Count];
        ShardLoadSnapshot[] snapshots = new ShardLoadSnapshot[shards.Count];
        int[] roomCntByShard = new int[shards.Count];
        long[] jobUsByShard = new long[shards.Count];
        Dictionary<string, long> roomLoadById = new();
        HashSet<string> seenRoomIds = new();
        long totalLoad = 0;
        int totalRooms = 0;
        bool overrunTriggered = false;

        for (int i = 0; i < shards.Count; i++)
        {
            snapshots[i] = shards[i].TakeLoadSnapshot();

            if (snapshots[i].ConsecutiveOverrunCnt >= ShardBalanceConfig.OverrunTriggerCnt)
            {
                overrunTriggered = true;
            }

            long jobDeltaSum = 0;
            long jobCostSum = 0;

            foreach (var room in shards[i].Rooms.Values)
            {
                long jobDelta = ConsumeJobDelta(room);
                long roomLoad = CalcRoomLoad(room, jobDelta);

                loadByShard[i] += roomLoad;
                totalLoad += roomLoad;
                totalRooms++;
                roomCntByShard[i]++;

                jobDeltaSum += jobDelta;
                jobCostSum += jobDelta * room.JobCostEmaUs;

                seenRoomIds.Add(room.Id);
                roomLoadById[room.Id] = roomLoad;
            }

            // 작업 처리 비용 표시: 작업 델타 가중 평균 JobCostEmaUs (μs, 주기 내 작업이 없으면 0)
            jobUsByShard[i] = jobDeltaSum > 0 ? jobCostSum / jobDeltaSum : 0;
        }

        // 2) 마지막 읽기값을 이번 주기에 관측된 룸만 남기도록 무조건 재구축 (사라진 룸의 잔여 항목 잔존 방지)
        Dictionary<string, long> newLastJobCntById = new();

        foreach (var id in seenRoomIds)
        {
            newLastJobCntById[id] = _lastJobCntById[id]; // ConsumeJobDelta가 관측 룸을 항상 기록하므로 키 존재 보장
        }

        _lastJobCntById = newLastJobCntById;

        // 룸이 없는 구간의 지속 상태를 다음 구간에 이월하지 않는다
        if (totalRooms == 0)
        {
            _sustainCnt = 0;
            _prevSnapshots = Array.Empty<ShardLoadSnapshot>(); // 다음 구간 첫 주기의 증가분 기준을 리셋
            return;
        }

        // 3) 최대/최소 부하 샤드
        int maxIdx = 0;
        int minIdx = 0;

        for (int i = 1; i < shards.Count; i++)
        {
            if (loadByShard[i] > loadByShard[maxIdx]) maxIdx = i;
            if (loadByShard[i] < loadByShard[minIdx]) minIdx = i;
        }

        long maxLoad = loadByShard[maxIdx];
        long minLoad = loadByShard[minIdx];

        long avgLoad = totalLoad / shards.Count;

        // 평균 부하가 0이면 편차를 정의할 수 없음 - 0 처리 (0 나누기 없음, 이동도 없음)
        double deviation = avgLoad > 0 ? (double)(maxLoad - minLoad) / avgLoad : 0.0;

        // 4) 지속 판정 및 룸 이동 시도
        string? movedRoomId = null;
        int movedFromIdx = 0;
        int movedToIdx = 0;

        if (deviation < ShardBalanceConfig.DeviationThreshold && !overrunTriggered)
        {
            _sustainCnt = 0;
        }
        else
        {
            _sustainCnt++;

            // 오버런 트리거는 편차 지속 조건을 우회한다
            if ((_sustainCnt >= ShardBalanceConfig.SustainSeconds || overrunTriggered) &&
                now - _lastMoveAtMs >= ShardBalanceConfig.GlobalCooldownMs)
            {
                int movedCnt = 0;

                // 같은 판정에서 큐잉된 이동이 아직 실행 전인 룸을 재선택하지 않도록 한다
                HashSet<string> selectedRoomIds = new();

                while (movedCnt < ShardBalanceConfig.MaxRoomsPerDecision)
                {
                    // MoveRoom은 비동기 큐잉이라 Rooms/LastMovedAtMs가 아직 갱신되지 않았을 수 있으므로
                    // 이동이 반영된 loadByShard 기준으로 현재 전체 극단 샤드를 매 반복 재계산한다
                    int curMaxIdx = 0;
                    int curMinIdx = 0;

                    for (int i = 1; i < loadByShard.Length; i++)
                    {
                        if (loadByShard[i] > loadByShard[curMaxIdx]) curMaxIdx = i;
                        if (loadByShard[i] < loadByShard[curMinIdx]) curMinIdx = i;
                    }

                    // 현재 스프레드가 0이면 더 이상 개선할 수 없다
                    if (curMaxIdx == curMinIdx || loadByShard[curMaxIdx] == loadByShard[curMinIdx]) break;

                    string? result = TryMoveOneRoom(shards, loadByShard, roomLoadById, curMaxIdx, curMinIdx, now, selectedRoomIds);

                    if (result == null) break;

                    movedRoomId = result;
                    movedFromIdx = curMaxIdx;
                    movedToIdx = curMinIdx;
                    movedCnt++;
                }

                if (movedCnt > 0)
                {
                    _sustainCnt = 0;    // 히스테리시스: 이동 후 지속 카운트 리셋
                    _lastMoveAtMs = now;
                }
            }
        }

        // 5) 측정 지표 로그 (활성화된 경우 주기당 1회, 룸이 존재할 때만)
        if (ShardBalanceConfig.EnableMetricsLog)
        {
            LogMetrics(snapshots, roomCntByShard, jobUsByShard, deviation, movedRoomId, movedFromIdx, movedToIdx);
        }

        _prevSnapshots = snapshots;
    }

    // 최대 부하 샤드에서 이동 시 "전체" 샤드 부하 스프레드가 가장 줄어드는 룸 하나를 골라 이동한다
    // 반환값: 이동한 룸 Id (이동하지 않으면 null)
    private string? TryMoveOneRoom(IReadOnlyList<Shard> shards, long[] loadByShard, Dictionary<string, long> roomLoadById, int maxIdx, int minIdx, long now, HashSet<string> selectedRoomIds)
    {
        long maxLoad = loadByShard[maxIdx];
        long minLoad = loadByShard[minIdx];
        long spreadBefore = maxLoad - minLoad;

        // 이동으로 변하지 않는 나머지 샤드의 최대/최소 부하 (전체 스프레드 계산용, 호출당 1회)
        long othersMax = long.MinValue;
        long othersMin = long.MaxValue;

        for (int i = 0; i < loadByShard.Length; i++)
        {
            if (i == maxIdx || i == minIdx) continue;

            if (loadByShard[i] > othersMax) othersMax = loadByShard[i];
            if (loadByShard[i] < othersMin) othersMin = loadByShard[i];
        }

        Room? bestRoom = null;
        long bestCost = 0;
        long bestSpreadAfter = 0;

        foreach (var room in shards[maxIdx].Rooms.Values)
        {
            // 소멸 직후 창구 최적화, 정확성은 M/등록 job 가드가 보장
            if (room.IsDestroyed) continue;

            // 한 번도 이동하지 않은 룸은 쿨다운 미적용
            if (room.LastMovedAtMs != 0 && now - room.LastMovedAtMs < ShardBalanceConfig.RoomMoveCooldownMs) continue;

            // 같은 판정에서 큐잉된 이동이 아직 실행 전인 룸을 재선택하지 않도록 한다
            if (selectedRoomIds.Contains(room.Id)) continue;
            if (!roomLoadById.TryGetValue(room.Id, out long cost)) continue;

            // 이동 후의 전체 샤드 부하 스프레드 - maxIdx/minIdx만 변하고 나머지는 그대로다
            // 극단값이 연동되어 관계가 뒤집히거나(crossover) 다른 샤드가 새 극단이 되는 경우도 반영된다
            long movedLoad = maxLoad - cost;    // 이동 후 소스 샤드 부하
            long receivedLoad = minLoad + cost; // 이동 후 타깃 샤드 부하

            long maxAfterAll = movedLoad;
            if (receivedLoad > maxAfterAll) maxAfterAll = receivedLoad;
            if (othersMax > maxAfterAll) maxAfterAll = othersMax;

            long minAfterAll = movedLoad;
            if (receivedLoad < minAfterAll) minAfterAll = receivedLoad;
            if (othersMin < minAfterAll) minAfterAll = othersMin;

            long spreadAfter = maxAfterAll - minAfterAll;

            // 최소 개선 폭 미만이면 제외 (플래피킹 방지) - 전체 스프레드 기준
            if (spreadAfter >= spreadBefore - ShardBalanceConfig.MinimumImprovementUs) continue;

            if (bestRoom == null || spreadAfter < bestSpreadAfter)
            {
                bestRoom = room;
                bestCost = cost;
                bestSpreadAfter = spreadAfter;
            }
        }

        if (bestRoom == null) return null;

        // 이 룸은 이번 판정에서 이동 큐잉됨 - 같은 판정의 다음 반복에서 재선택되지 않도록 기록
        selectedRoomIds.Add(bestRoom.Id);

        _shardManager.MoveRoom(bestRoom, shards[minIdx]);

        // 이동 반영 (같은 판정에서 추가 이동 시 사용)
        loadByShard[maxIdx] -= bestCost;
        loadByShard[minIdx] += bestCost;

        return bestRoom.Id;
    }

    // 주기당 1회, 전체 샤드의 측정 지표를 한 줄로 요약 출력
    // overrun=a/b: a = 이번 주기 오버런 증가분, b = 연속 오버런 횟수 (틱 주기 50ms 초과 빈도 노출)
    private void LogMetrics(ShardLoadSnapshot[] snapshots, int[] roomCntByShard, long[] jobUsByShard, double deviation, string? movedRoomId, int moveFromIdx, int moveToIdx)
    {
        // 첫 측정 주기는 비교 기준이 없으므로 증가분을 0으로 표시
        bool hasPrev = _prevSnapshots.Length == snapshots.Length;

        StringBuilder line = new("[ShardBalance]");

        for (int i = 0; i < snapshots.Length; i++)
        {
            ShardLoadSnapshot snapshot = snapshots[i];
            ShardLoadSnapshot prev = hasPrev ? _prevSnapshots[i] : default;

            long jobDelta = hasPrev ? snapshot.ProcessedJobCnt - prev.ProcessedJobCnt : 0;
            long overrunDelta = hasPrev ? snapshot.TotalOverrunCnt - prev.TotalOverrunCnt : 0;

            if (jobDelta < 0) jobDelta = 0;
            if (overrunDelta < 0) overrunDelta = 0;

            double jobsPerSec = jobDelta * 1000.0 / ShardBalanceConfig.MeasurementPeriodMs;

            line.Append(i == 0 ? " " : " | ");
            line.Append($"s{i} rooms={roomCntByShard[i]} tick={snapshot.TickSweepEmaUs}us jobs={jobsPerSec:F0}/s jobus={jobUsByShard[i]} q={snapshot.QueueDepth} overrun={overrunDelta}/{snapshot.ConsecutiveOverrunCnt}");
        }

        line.Append($" | dev={deviation:F2}");

        if (movedRoomId != null)
        {
            line.Append($" move=s{moveFromIdx}->s{moveToIdx}({movedRoomId})");
        }

        Console.WriteLine(line.ToString());
    }

    // 리밸런서가 보관한 마지막 읽기값 기준으로 주기 내 작업 수 델타를 소비한다
    private long ConsumeJobDelta(Room room)
    {
        long current = room.JobCount;
        long last = _lastJobCntById.TryGetValue(room.Id, out var value) ? value : 0;

        _lastJobCntById[room.Id] = current;

        long delta = current - last;

        return delta > 0 ? delta : 0;
    }

    // 룸 하나의 부하 기여도 (μs/tick 환산, 상대 비교용)
    // 주기 내 작업 비용을 측정 윈도우로 정규화: jobDelta x JobCostEmaUs x 1000 / (측정 주기 ms x 초당 틱 수)
    private static long CalcRoomLoad(Room room, long jobDelta)
    {
        double jobLoadPerTick = jobDelta * (double)room.JobCostEmaUs * 1000.0 / (ShardBalanceConfig.MeasurementPeriodMs * ShardBalanceConfig.TicksPerSecond);

        return room.TickCostEmaUs + (long)jobLoadPerTick;
    }
}
