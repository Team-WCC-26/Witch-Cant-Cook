using Protocol;

namespace Server;

internal class DishManager
{
    private readonly Room _room;
    private readonly TimerManager _timerManager;

    private IReadOnlyDictionary<int, DishData> _dishses => ServerContext.Instance.DataBase.Dishes;
    private IReadOnlyDictionary<IngredientStatePair, int> _recipes => ServerContext.Instance.DataBase.Recipes;
    private IReadOnlyDictionary<int, List<RecipeGroup>> _recipeGroups => ServerContext.Instance.DataBase.RecipeGroups;
    private readonly Random _random = new();

    private List<DishData> _currentDishes = new();
    private int _totalWeight;

    private Dictionary<IngredientStatePair, Queue<TimerHandle>> _timeLimitHandleDict = new();
    private TimerHandle _spawnDelayHandle;
    private bool _running = false;

    public DishManager(Room room, TimerManager timerManager)
    {
        _room = room;
        _timerManager = timerManager;
    }

    public void Start()
    {
        if (_running) return;

        _running = true;
        OnSelectTimer();
    }

    public void Stop()
    {
        _running = false;

        foreach (var handles in _timeLimitHandleDict.Values)
        {
            while (handles.TryDequeue(out var handle))
            {
                _timerManager.Cancel(handle);
            }
        }

        _timerManager.Cancel(_spawnDelayHandle);
    }

    private void ScheduleSelect(int delayMs)
    {
        if (!_running) return;

        _spawnDelayHandle = _timerManager.Schedule(delayMs, this, static dm => dm.OnSelectTimer());
    }

    public void SetStage(int stage)
    {
        if (!_recipeGroups.TryGetValue(stage, out var group)) throw new ArgumentException($"존재하지 않는 스테이지입니다. Stage: {stage}");

        _currentDishes.Clear();

        foreach (var data in group)
        {
            _currentDishes.Add(_dishses[data.RecipeId]);
        }

        _totalWeight = _currentDishes.Sum(x => x.SpawnWeight);
    }

    public int GetRandomRecipe()
    {
        if (_currentDishes.Count == 0) throw new InvalidOperationException("사용가능한 레시피가 없습니다.");

        int value = _random.Next(_totalWeight);

        foreach (var dish in _currentDishes)
        {
            value -= dish.SpawnWeight;

            if (value < 0) return dish.Id;
        }

        throw new InvalidOperationException("재료 선택에 실패했습니다.");
    }

    public void SubmitDish(IngredientStatePair dish)
    {
        if (_timeLimitHandleDict.TryGetValue(dish, out var handles) && handles.TryDequeue(out var handle))
        {
            _timerManager.Cancel(handle);

            BroadCastDishState(_recipes[dish], DishState.Success);
        }
    }

    private void OnSelectTimer()
    {
        if (!_running) return;

        var recipeId = GetRandomRecipe();
        var dishData = _dishses[recipeId];

        IngredientStatePair dish = new(dishData.IngredientId, dishData.ConditionFlag);

        if (!_timeLimitHandleDict.TryGetValue(dish, out var queue))
        {
            queue = new();
            _timeLimitHandleDict[dish] = queue;
        }

        queue.Enqueue(_timerManager.Schedule(dishData.TimeLimit, dish, OnDishFaild));

        BroadCastDishState(recipeId, DishState.Order);

        ScheduleSelect(dishData.SpawnDelay);
    }

    private void OnDishFaild(IngredientStatePair dish)
    {
        if (!_timeLimitHandleDict.TryGetValue(dish, out var queue) || !queue.TryDequeue(out _)) return;

        if (_recipes.TryGetValue(dish, out var recipe))
        {
            BroadCastDishState(recipe, DishState.Fail);
        }
    }

    private void BroadCastDishState(int id, DishState state)
    {
        DishStatePacket packet = new()
        {
            RecipeId = id,
            State = state
        };

        _room.BroadCast(PacketSerializer.Serialize(packet, true));
    }
}
