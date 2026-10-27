namespace Server;

public class ShardManager
{
    private readonly int _shardCnt = 8;
    private Shard[] _shards;
    

    // 리밸런서가 샤드 목록을 순회하기 위한 읽기 전용 뷰
    public IReadOnlyList<Shard> Shards => _shards;

    public ShardManager()
    {
        _shards = new Shard[_shardCnt];

        for (int i = 0; i < _shardCnt; i++)
        {
            Shard shard = new();
            _ = Task.Run(shard.StartProcess);
            _shards[i] = shard;
        }

        // 리밸런서 시작 (Enabled는 주기마다 다시 검사하므로 실행 중 변경 가능)
        ShardRebalancer rebalancer = new(this);
        _ = Task.Run(rebalancer.RunLoop);
    }

    public void RegisterRoom(Room room)
    {
        GetShard().RegisterRoom(room);
    }

    // 룸을 다른 샤드로 재배치 (리밸런서 전용)
    // 마이그레이션 작업 M을 소스 샤드에 push한다. M은 소스 샤드 스레드에서 실행되므로
    // 해당 룸의 기존 작업들 및 Tick과 직렬화된다 (작업 유실/중복/동시 실행 없음).
    public void MoveRoom(Room room, Shard target)
    {
        Shard source = room.GetShard();

        if (ReferenceEquals(source, target)) return;

        source.Push(() =>
        {
            // 소멸된 룸은 이동하지 않는다
            if (room.IsDestroyed) return;

            // 소스가 이미 바뀌어 있으면(이중 이동 등) 무시 - BeginMigration 이전에 검사
            if (!ReferenceEquals(room.GetShard(), source)) return;

            // 실제 이동이 수락된 경우에만 쿨다운 기록
            room.MarkMoved();

            // 1) 이 순간부터 새로 Push되는 작업은 target 샤드를 executor로 사용한다 (무주속 방지)
            room.BeginMigration(target);

            // 2) 소스 샤드에서 동기 제거 (타깃 등록보다 먼저 - 이중 Tick 방지, 멱등)
            source.TryRemoveRoom(room.Id);

            // 3) 타깃 샤드 스레드에서 등록 (소멸된 룸은 등록 생략)
            target.Push(() => target.TryAddRoom(room));
        });
    }

    // 초기 배치용 샤드 선택
    // 측정된 틱 비용 합이 가장 낮은 샤드를 선호하고, 측정값이 모두 0이면(서버 초기 상태) 종전처럼 룸 수 기준으로 선택
    private Shard GetShard()
    {
        if (_shards.All(s => s.EstimateTickLoadUs() == 0))
        {
            return _shards.OrderBy(s => s.RoomCnt).First();
        }

        return _shards.OrderBy(s => s.EstimateTickLoadUs()).First();
    }
}
