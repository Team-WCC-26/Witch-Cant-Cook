using Protocol;

namespace Server;

public class Pot() : ContainerTool(new MultiSlotStorage()), IFixedTool
{
    private const long _ejectDelayMs = 1000;
    private const int _trashIngredientId = 99999;

    private readonly List<Player> _players = new();
    private readonly HashSet<Entity> _autoEjectTargets = new();

    private bool _isCooking;
    private int _cookingDishIndex = -1;
    private Dish? _cookingDish;
    private TimerHandle _cookTimer;
    private TimerHandle _autoEjectTimer;

    private TimerManager _timerManager => Room.TimerManager;

    public bool IsCooking => _isCooking;

    public override bool Interact(Player player)
    {
        // 삽입은 C_EntityInsert, 꺼내기는 조리 완료/강제 배출로만 가능
        return false;
    }

    public override bool Insert(Entity entity)
    {
        if (entity == null || entity.IsDestroyed) return false;
        if (entity is Player) return false;
        if (!base.Insert(entity)) return false;

        if (_isCooking) return true;

        if (entity is Ingredient) return true;

        if (entity is Dish dish && dish.First == null)
        {
            TryStartCook(dish);

            return true;
        }

        PendAutoEject(entity);

        return true;
    }

    public override bool Remove(Entity entity)
    {
        _autoEjectTargets.Remove(entity);

        if (entity == _cookingDish)
        {
            CancelCook();
        }

        return base.Remove(entity);
    }

    public bool AddPlayer(Player player)
    {
        if (player.InsidePot != null) return false;
        if (_players.Contains(player)) return false;

        _players.Add(player);
        player.InsidePot = this;

        if (_players.Count >= 2) ForceEject();

        return true;
    }

    public void RemovePlayer(Player player)
    {
        if (_players.Remove(player))
        {
            player.InsidePot = null;
        }
    }

    public void ForceEject()
    {
        CancelCook();

        EjectAll();
    }

    private bool TryStartCook(Dish dish)
    {
        float maxHp = 0;
        bool hasIngredient = false;
        int dishIndex = 0;

        foreach (var entity in _storage)
        {
            if (entity == dish) break;

            if (entity is Ingredient ingredient)
            {
                hasIngredient = true;
                maxHp = MathF.Max(maxHp, ingredient.Stat.Hp);
            }

            dishIndex++;
        }

        if (!hasIngredient) return false;

        CancelAutoEject();

        _isCooking = true;
        _cookingDish = dish;
        _cookingDishIndex = dishIndex;

        long delayMs = (long)MathF.Ceiling(maxHp / Damage);

        _cookTimer = _timerManager.Schedule(delayMs, this, static t => t.CompleteCook());

        CookStartPacket packet = new()
        {
            ToolEntityId = EntityId,
            CookingTimeMs = delayMs
        };

        Room.BroadCast(PacketSerializer.Serialize(packet, true));

        return true;
    }

    private void CompleteCook()
    {
        _cookTimer = default;

        if (!_isCooking) return;

        if (_cookingDish == null || !_storage.Contains(_cookingDish))
        {
            CancelCook();
            return;
        }

        var dish = _cookingDish;

        List<Ingredient> targets = new();

        foreach (var entity in _storage)
        {
            if (entity == dish) break;

            if (entity is Ingredient ingredient && !ingredient.IsDestroyed)
            {
                targets.Add(ingredient);
            }
        }

        RecipeKey recipeKey = new(targets.Select(i => new IngredientStatePair(i.IngredientId, i.ProcessState)));

        if (!ServerContext.Instance.DataBase.IngredientCombinations.TryGetValue(recipeKey, out var resultId))
        {
            resultId = _trashIngredientId;
        }

        Ingredient result = Room.GenerateIngredient(resultId, out _);

        IngredientSpawnPacket spawnPacket = new()
        {
            EntityId = result.EntityId,
            IngredientID = resultId
        };

        Room.BroadCast(PacketSerializer.Serialize(spawnPacket, true));

        _isCooking = false;
        _cookingDish = null;
        _cookingDishIndex = -1;

        dish.Insert(result);

        foreach (var ingredient in targets)
        {
            ingredient.Destroy();
        }

        PotCookCompletePacket completePacket = new()
        {
            PotEntityId = EntityId,
            DishEntityId = dish.EntityId,
            ResultIngredientId = resultId
        };

        Room.BroadCast(PacketSerializer.Serialize(completePacket, true));

        EjectAll();
    }

    private void EjectAll()
    {
        CancelAutoEject();

        if (_storage.Count == 0 && _players.Count == 0) return;

        List<Entity> ejectEntities = new(_storage);
        List<Player> ejectPlayers = new(_players);

        _storage.Clear();
        _players.Clear();

        foreach (var entity in ejectEntities)
        {
            entity.Parent = null;
        }

        foreach (var player in ejectPlayers)
        {
            player.InsidePot = null;
        }

        BroadCastEject(ejectEntities, ejectPlayers);
    }

    private void PendAutoEject(Entity entity)
    {
        _autoEjectTargets.Add(entity);

        if (_autoEjectTimer.Id != 0) return;

        _autoEjectTimer = _timerManager.Schedule(_ejectDelayMs, this, static t => t.AutoEject());
    }

    private void AutoEject()
    {
        _autoEjectTimer = default;

        if (_isCooking)
        {
            _autoEjectTargets.Clear();
            return;
        }

        if (_autoEjectTargets.Count == 0) return;

        List<Entity> ejectTargets = new();

        foreach (var entity in _autoEjectTargets)
        {
            if (_storage.Contains(entity) && !entity.IsDestroyed)
            {
                ejectTargets.Add(entity);
            }
        }

        _autoEjectTargets.Clear();

        if (ejectTargets.Count == 0) return;

        foreach (var entity in ejectTargets)
        {
            entity.Parent = null;
        }

        BroadCastEject(ejectTargets, new List<Player>());
    }

    private void BroadCastEject(List<Entity> entities, List<Player> players)
    {
        PotEjectPacket packet = new()
        {
            PotEntityId = EntityId,
            EjectSeed = (int)TimeUtil.NowMs(),
            EjectDelayMs = _ejectDelayMs
        };

        foreach (var player in players)
        {
            packet.PlayerIds.Add(player.PlayerId);
        }

        foreach (var entity in entities)
        {
            packet.EntityIds.Add(entity.EntityId);
        }

        Room.BroadCast(PacketSerializer.Serialize(packet, true));
    }

    private void CancelCook()
    {
        if (!_isCooking) return;

        _timerManager.Cancel(_cookTimer);
        _cookTimer = default;

        _isCooking = false;
        _cookingDish = null;
        _cookingDishIndex = -1;
    }

    private void CancelAutoEject()
    {
        if (_autoEjectTimer.Id != 0)
        {
            _timerManager.Cancel(_autoEjectTimer);
            _autoEjectTimer = default;
        }

        _autoEjectTargets.Clear();
    }
}
