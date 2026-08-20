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

    private Dictionary<int, int> _beltDict = new();
    private List<IngredientData> _currentIngredients = new();
    private int _totalWeight;

    private TimerHandle _handle;
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
        ScheduleSpawn();
    }

    public void Stop()
    {
        _running = false;
        _timerManager.Cancel(_handle);
    }

    private void ScheduleSpawn()
    {
        if (!_running) return;

        _handle = _timerManager.Schedule(_spawnInterval, this, static s => s.OnSpawnTimer());
    }

    public void SetStage(int stage)
    {
        if (!_ingredientGroups.TryGetValue(stage, out var group)) throw new ArgumentException($"존재하지 않는 스테이지입니다. Stage: {stage}");

        _beltDict.Clear();
        _currentIngredients.Clear();

        foreach (var data in group)
        {
            _beltDict[data.IngredientId] = data.BeltId;
            _currentIngredients.Add(_ingredients[data.IngredientId]);
        }

        _totalWeight = _currentIngredients.Sum(x => x.SpawnWeight);
    }

    public (int, int) GetRandomIngredient()
    {
        if (_currentIngredients.Count == 0) throw new InvalidOperationException("스폰 가능한 재료가 없습니다.");

        int value = _random.Next(_totalWeight);

        foreach (var ingredient in _currentIngredients)
        {
            value -= ingredient.SpawnWeight;

            if (value < 0) return ( ingredient.Id, _beltDict[ingredient.Id] );
        }

        throw new InvalidOperationException("재료 선택에 실패했습니다.");
    }

    private void OnSpawnTimer()
    {
        if (!_running) return;

        var (ingredientId, beltId) = GetRandomIngredient();

        var ingredient = _room.GenerateIngredient(ingredientId, out var entityId);

        IngredientConveySpawnPacket packet = new()
        {
            ConveyId = beltId,
            EntityId = entityId,
            IngredienteId = ingredientId,
        };

        _room.BroadCast(PacketSerializer.Serialize(packet, true));

        ScheduleSpawn();
    }
}
