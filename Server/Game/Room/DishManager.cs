using Protocol;

namespace Server;

internal class DishManager
{
    private readonly Room _room;
    private readonly TimerManager _timerManager;

    private IReadOnlyDictionary<int, DishData> _dishses => ServerContext.Instance.DataBase.Dishes;
    //private IReadOnlyDictionary<IngredientStatePair, int> _recipes => ServerContext.Instance.DataBase.Recipes;
    private IReadOnlyDictionary<int, List<RecipeGroup>> _recipeGroups => ServerContext.Instance.DataBase.RecipeGroups;
    private IReadOnlyDictionary<int, StageData> _stages => ServerContext.Instance.DataBase.Stages;
    private readonly Random _random = new();

    private List<DishData> _currentDishes = new();
    private int _totalWeight;

    private int _goal = 0;

    private Dictionary<int, Queue<TimerHandle>> _timeLimitHandleDict = new();
    private int _orderCnt = 0;
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

        _orderCnt = 0;

        _timerManager.Cancel(_spawnDelayHandle);
    }

    // 새 주문 선택만 중지한다 (진행 중인 주문은 유지 — 스테이지 종료 후 제출 유예 기간 동안 접수)
    public void StopSelect()
    {
        _running = false;

        _timerManager.Cancel(_spawnDelayHandle);
    }

    private void ScheduleSelect(int delayMs)
    {
        if (!_running) return;

        _spawnDelayHandle = _timerManager.Schedule(delayMs, this, static dm => dm.OnSelectTimer(), _orderCnt >= 3);
    }

    public void SetStage(int stage)
    {
        if (!_recipeGroups.TryGetValue(stage, out var group)) throw new ArgumentException($"존재하지 않는 스테이지입니다. Stage: {stage}");
        if (!_stages.TryGetValue(stage, out var stageData)) throw new ArgumentException($"존재하지 않는 스테이지입니다. Stage: {stage}");

        _goal = stageData.Goal;

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

    public bool IsStageSuccess()
    {
        return _room.PendingMoney >= _goal;
    }

    // 현재 진행 중인 주문 목록 (레시피 ID와 남은 시한) — 재접속 스냅샷 수집용
    public List<(int RecipeId, long RemainingMs)> GetActiveOrders()
    {
        List<(int, long)> orders = new();

        foreach (var pair in _timeLimitHandleDict)
        {
            foreach (var handle in pair.Value)
            {
                orders.Add((pair.Key, _timerManager.RemainingTime(handle)));
            }
        }

        return orders;
    }

    // 다음 주문 생성 딜레이까지 남은 시간 — 재접속 스냅샷 수집용 (딜레이 대기 중이 아니면 0)
    public long GetNextOrderRemainingMs()
    {
        if (!_running) return 0;
        if (!_timerManager.CheckHandleValid(_spawnDelayHandle)) return 0;

        return _timerManager.RemainingTime(_spawnDelayHandle);
    }

    public void SubmitDish(int recipeId)
    {
        if (_timeLimitHandleDict.TryGetValue(recipeId, out var handles) && handles.TryDequeue(out var handle))
        {
            _timerManager.Cancel(handle);
            
            if (_orderCnt-- == 3)
            {
                _timerManager.Resume(_spawnDelayHandle);
            }

            BroadCastDishState(recipeId, DishState.Success);

            _room.AddPendingMoney(_dishses[recipeId].Money);
        }
        else
        {
            BroadCastDishState(-1, DishState.Fail);
        }
    }

    private void OnSelectTimer()
    {
        if (!_running) return;

        var recipeId = GetRandomRecipe();
        var dishData = _dishses[recipeId];

        if (!_timeLimitHandleDict.TryGetValue(recipeId, out var queue))
        {
            queue = new();
            _timeLimitHandleDict[recipeId] = queue;
        }

        queue.Enqueue(_timerManager.Schedule(dishData.TimeLimit * 1000, recipeId, OnDishFaild));
        _orderCnt++;

        BroadCastDishState(recipeId, DishState.Order);

        ScheduleSelect(dishData.SpawnDelay * 1000);
    }

    private void OnDishFaild(int recipeId)
    {
        if (!_timeLimitHandleDict.TryGetValue(recipeId, out var queue) || !queue.TryDequeue(out _)) return;

        if (_dishses.TryGetValue(recipeId, out var recipe))
        {
            BroadCastDishState(recipeId, DishState.Fail);
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
