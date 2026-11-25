using Protocol;

namespace Server;

public class IngredientSpawner
{
    private readonly Room _room;
    private readonly TimerManager _timerManager;
    private int _spawnInterval;

    // 미출현 재료 1회당 가중치 증가 배율
    private const int _missWeightMultiplier = 2;

    // 재료 가중치의 최댓값
    private const int _maxWeight = 64;

    private IReadOnlyDictionary<int, List<IngredientGroup>> _ingredientGroups => ServerContext.Instance.DataBase.IngredientGroups;
    private IReadOnlyDictionary<int, IngredientData> _ingredients => ServerContext.Instance.DataBase.Ingredients;
    private readonly Random _random = new();

    private Dictionary<int, BeltSchedule> _beltSchedules = new();

    private bool _running = false;

    public IngredientSpawner(Room room, TimerManager timerManager, int spawnInterval)
    {
        _room = room;
        _timerManager = timerManager;
        _spawnInterval = spawnInterval;
    }

    public void Start()
    {
        if (_running) return;

        _running = true;

        foreach (var schedule in _beltSchedules.Values)
        {
            schedule.Schedule();
        }
    }

    public void Stop()
    {
        _running = false;

        foreach (var schedule in _beltSchedules.Values)
        {
            _timerManager.Cancel(schedule.Handle);
        }
    }

    public void SetStage(int stage)
    {
        if (!_ingredientGroups.TryGetValue(stage, out var group)) throw new ArgumentException($"존재하지 않는 스테이지입니다. Stage: {stage}");

        foreach (var schedule in _beltSchedules.Values)
        {
            schedule.Clear();
        }

        foreach (var data in group)
        {
            if (!_beltSchedules.TryGetValue(data.BeltId, out var schedule))
            {
                schedule = new(this, _timerManager, _spawnInterval, data.BeltId);
                _beltSchedules[data.BeltId] = schedule;
            }

            schedule.AddData(_ingredients[data.IngredientId]);
        }

        if (_running)
        {
            foreach (var schedule in _beltSchedules.Values)
            {
                schedule.Schedule();
            }
        }
    }

    // 벨트별 스포너 식별자와 다음 생성까지 남은 시간 — 재접속 스냅샷 수집용
    public List<SpawnerSnapshot> GetSpawnRemainingList()
    {
        List<SpawnerSnapshot> spawners = new();

        foreach (var schedule in _beltSchedules.Values)
        {
            spawners.Add(new SpawnerSnapshot()
            {
                ConveyId = schedule.BeltId,
                RemainingMs = _timerManager.RemainingTime(schedule.Handle)
            });
        }

        return spawners;
    }

    private void OnSpawnTimer(BeltSchedule schedule)
    {
        if (!_running) return;

        var (ingredientId, beltId) = schedule.GetRandomIngredient();

        _room.GenerateIngredient(ingredientId, out var entityId);

        IngredientConveySpawnPacket packet = new()
        {
            ConveyId = beltId,
            EntityId = entityId,
            IngredientId = ingredientId,
        };

        _room.BroadCast(PacketSerializer.Serialize(packet, true));

        // 재접속 유예 창 재생 목록 기록 (타이머 콜백 = 룸 Tick과 같은 샤드 스레드에서 관측만 수행)
        // 원본 패킷도 Transform을 전송하지 않으므로 위치는 ConveyId(벨트 스폰 지점)로 산출한다
        _room.RecordReplayEvent(new ReplayEventSnapshot()
        {
            Kind = ReplayEventKind.ConveySpawn,
            EntityId = packet.EntityId,
            IngredientId = packet.IngredientId,
            ConveyId = packet.ConveyId
        });

        schedule.Schedule();
    }

    private class BeltSchedule
    {
        public TimerHandle Handle;

        public int BeltId => _beltId;

        private List<IngredientData> _currentIngredients = new();
        // 재료별 현재 가중치 — 초기 가중치는 IngredientData.SpawnWeight(데이터테이블 기본 가중치)와 구분하여 관리한다
        private Dictionary<int, int> _currentWeights = new();

        private readonly IngredientSpawner _spawner;
        private readonly TimerManager _timerManager;
        private readonly int _interval;
        private readonly int _beltId;

        public BeltSchedule(IngredientSpawner spawner, TimerManager timerManager, int interval, int beltId)
        {
            _spawner = spawner;
            _timerManager = timerManager;
            _interval = interval;
            _beltId = beltId;
        }

        public void AddData(IngredientData data)
        {
            if (_currentWeights.ContainsKey(data.Id)) return; // 같은 벨트에 동일 재료가 중복 등록되지 않도록 한다

            _currentIngredients.Add(data);
            _currentWeights.Add(data.Id, data.SpawnWeight);
        }

        public void Schedule()
        {
            if (_currentIngredients.Count == 0) return;

            Handle = _timerManager.Schedule(_interval, this, static s => s._spawner.OnSpawnTimer(s));
        }

        public (int, int) GetRandomIngredient()
        {
            if (_currentIngredients.Count == 0) throw new InvalidOperationException("스폰 가능한 재료가 없습니다.");

            // 매 스폰마다 현재 가중치를 기준으로 합계를 다시 계산한다
            int totalWeight = 0;

            foreach (var ingredient in _currentIngredients)
            {
                totalWeight += _currentWeights[ingredient.Id];
            }

            int value = _spawner._random.Next(totalWeight);

            foreach (var ingredient in _currentIngredients)
            {
                value -= _currentWeights[ingredient.Id];

                if (value < 0)
                {
                    // 스폰된 재료의 가중치 갱신: 출현한 재료는 초기 가중치로 되돌리고, 미출현 재료는 배율만큼 증가시킨다. 갱신된 값은 다음 추첨부터 사용한다
                    UpdateWeights(ingredient);

                    return (ingredient.Id, _beltId);
                }
            }

            throw new InvalidOperationException("재료 선택에 실패했습니다.");
        }

        public void Clear()
        {
            _timerManager.Cancel(Handle);
            _currentIngredients.Clear();
            _currentWeights.Clear();
        }

        // 스폰 후 가중치 갱신: 출현한 재료는 초기 가중치로 되돌리고, 미출현 재료는 배율만큼 증가시켜 상한을 제한한다
        private void UpdateWeights(IngredientData spawned)
        {
            foreach (var ingredient in _currentIngredients)
            {
                if (ingredient.Id == spawned.Id)
                {
                    _currentWeights[ingredient.Id] = spawned.SpawnWeight;
                }
                else
                {
                    _currentWeights[ingredient.Id] = Math.Min(_currentWeights[ingredient.Id] * _missWeightMultiplier, _maxWeight);
                }
            }
        }
    }
}
