using Protocol;

namespace Server;

public class IngredientSpawner
{
    private readonly Room _room;
    private readonly TimerManager _timerManager;
    private int _spawnInterval;

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

        schedule.Schedule();
    }

    private class BeltSchedule
    {
        public TimerHandle Handle;

        private List<IngredientData> _currentIngredients = new();
        private int _totalWeight = 0;

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
            _currentIngredients.Add(data);
            _totalWeight += data.SpawnWeight;
        }

        public void Schedule()
        {
            if (_currentIngredients.Count == 0) return;

            Handle = _timerManager.Schedule(_interval, this, static s => s._spawner.OnSpawnTimer(s));
        }

        public (int, int) GetRandomIngredient()
        {
            if (_currentIngredients.Count == 0) throw new InvalidOperationException("스폰 가능한 재료가 없습니다.");

            int value = _spawner._random.Next(_totalWeight);

            foreach (var ingredient in _currentIngredients)
            {
                value -= ingredient.SpawnWeight;

                if (value < 0) return (ingredient.Id, _beltId);
            }

            throw new InvalidOperationException("재료 선택에 실패했습니다.");
        }

        public void Clear()
        {
            _timerManager.Cancel(Handle);
            _currentIngredients.Clear();
            _totalWeight = 0;
        }
    }
}
