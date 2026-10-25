using Protocol;

namespace Server;

public class Room
{
    public string Id { get; }
    public string Name { get; }
    public string Password { get; }

    public IReadOnlyList<Player> Players => _players;
    private readonly List<Player> _players = new();
    public readonly int MaxPlayerCount = 2;

    public IReadOnlyDictionary<long, Entity> Entities => _entities;
    private readonly Dictionary<long, Entity> _entities = new();

    private readonly HashSet<Entity> _dirtyEntities = new();

    public int PlayerCnt => _playerCnt;
    private int _playerCnt = 0;
    private int _tick = 0;

    // 룸 공용 재화 (요리 제출 성공 시 지급, 주방 업그레이드에 사용)
    public int Money => _money;
    private int _money = 0;

    // 스테이지 종료 시 보유량에 정산되는 임시 보유량 (획득 예정량)
    public int PendingMoney => _pendingMoney;
    private int _pendingMoney = 0;

    private long _nextEntityId = 0;

    // 룸 소멸 여부 (RoomManager.RemoveRoom에서만 설정되며 되돌리지 않음)
    // 주의: Stop()은 스테이지 정리일 뿐 소멸이 아니므로 이 플래그를 건드리지 않는다
    public bool IsDestroyed => _destroyed;
    private volatile bool _destroyed = false;

    // 작업 실행 샤드는 JobQueue가 단일 출처로 관리한다
    private readonly JobQueue _jobQueue;

    private readonly Dictionary<DoorId, Door> _doors = new();

    public TimerManager TimerManager => _timerManager;
    private TimerManager _timerManager = new();

    // === 부하 측정 (소유 샤드 스레드 단일 작성자, 리밸런서는 근사 읽기) ===
    public long TickCostEmaUs => Volatile.Read(ref _tickCostEmaUs);
    private long _tickCostEmaUs = 0; // Room.Tick 비용 EMA (μs)

    public long JobCostEmaUs => Volatile.Read(ref _jobCostEmaUs);
    private long _jobCostEmaUs = 0; // 작업 1개당 비용 EMA (μs)

    public long JobCount => Volatile.Read(ref _jobCount);
    private long _jobCount = 0; // 누적 실행 작업 수

    private const long StagePlayTimeMs = 4 * 60 * 1000;

    private TimerHandle? _stageTimer;

    private IngredientSpawner _ingredientSpawner;
    private DishManager _dishManager;

    public Room(string id, string name, string password)
    {
        Id = id;
        Name = name;
        Password = password;

        _jobQueue = new(this);
        _ingredientSpawner = new(this, _timerManager, 2000);
        _dishManager = new(this, _timerManager);
    }

    public void Init()
    {
        _players.Clear();
        _playerCnt = 0;
        _tick = 0;
        _money = 0;
        _pendingMoney = 0;
        _nextEntityId = 0;
        _doors.Clear();

        _doors[DoorId.Lobby] = new(DoorId.Lobby, 2, 3, _timerManager, HandleDoorOpened);
        _doors[DoorId.Kitchen] = new(DoorId.Kitchen, 2, 3, _timerManager, HandleDoorOpened);

        SetStage(1);
    }

    public void Start()
    {
        if (_stageTimer != null)
        {
            _timerManager.Cancel(_stageTimer.Value);
        }

        _stageTimer = _timerManager.Schedule(StagePlayTimeMs, this, static room => room.EndStage());

        _dishManager.Start();
        _ingredientSpawner.Start();
    }

    public void Stop()
    {
        _dishManager.Stop();
        _ingredientSpawner.Stop();
        _timerManager.Clear();
        _stageTimer = null;

        List<Entity> entities = new(_entities.Values);
        foreach (var entity in entities)
        {
            entity.Parent = null;
            if (entity is Ingredient ingredient)
            {
                ingredient.IngredientBehaviour?.Clear();
            }
        }

        _entities.Clear();
        _dirtyEntities.Clear();
    }

    public void Tick(long deltaTime)
    {
        _timerManager.Tick(deltaTime);

        WorldStatePacket packet = new()
        {
            Tick = _tick
        };

        foreach (var player in _players)
        {
            var mask = player.ConsumeDirtyMask();
            player.WriteSnapShot(packet, mask);
        }

        foreach (var entity in _dirtyEntities)
        {
            var mask = entity.ConsumeDirtyMask();
            entity.WriteSnapShot(packet, mask);
        }

        _dirtyEntities.Clear();

        BroadCast(PacketSerializer.Serialize(packet, true));

        FlushSend();

        _tick++;
    }

    public bool IsEnable()
    {
        return _playerCnt >= 0 && _playerCnt < MaxPlayerCount;
    }

    public void PushJob(Action job) => _jobQueue.Push(job);

    public void InitShard(Shard shard) => _jobQueue.InitShard(shard);

    // 마이그레이션 시작 (소스 샤드 스레드의 마이그레이션 작업 M 내부에서 호출)
    public void BeginMigration(Shard target) => _jobQueue.BeginMigration(target);

    public Shard GetShard()
    {
        return _jobQueue.CurrentShard!;
    }

    // 마지막 재배치 시각 (리밸런서가 기록, 룸 단위 재배치 쿨다운용)
    public long LastMovedAtMs { get; private set; }

    public void MarkMoved() => LastMovedAtMs = TimeUtil.NowMs();

    // 룸 소멸 표시 (RoomManager.RemoveRoom에서만 호출 - 이후 실행되는 샤드 등록 job이 등록하지 않도록)
    public void MarkDestroyed() => _destroyed = true;

    // 샤드 루프가 Room.Tick 하나를 실행한 뒤 호출 (소유 샤드 스레드 전용)
    public void RecordTickCost(long costUs)
    {
        _tickCostEmaUs += (long)((costUs - _tickCostEmaUs) * ShardBalanceConfig.EmaAlpha);
    }

    // JobQueue가 작업 하나를 실행한 뒤 호출 (소유 샤드 스레드 전용)
    public void RecordJobExecuted(long costUs)
    {
        _jobCostEmaUs += (long)((costUs - _jobCostEmaUs) * ShardBalanceConfig.EmaAlpha);
        _jobCount++;
    }

    public void MakeDirty(Entity entity)
    {
        _dirtyEntities.Add(entity);
    }

    public void SetStage(int stage)
    {
        _ingredientSpawner.SetStage(stage);
        _dishManager.SetStage(stage);
    }

    public Ingredient GenerateIngredient(int id, out long entityId)
    {
        Ingredient ingredient = new();
        entityId = GenerateEntityId();

        RegisterEntity(entityId, ingredient);
        ingredient.InitIngredientId(id);

        return ingredient;
    }

    public Tool? GenerateTool(int id, out long entityId)
    {
        Tool? tool;

        switch (id)
        {
            case 10:
                tool = new Knife();
                break;

            case 20:
                tool = new Pan();
                break;

            case 30:
                tool = new Stove();
                break;

            case 40:
                tool = new Pot();
                break;

            case 50:
                tool = new Dish();
                break;

            case 60:
                tool = new CounterTop();
                break;

            case 70:
                tool = new JumpPad();
                break;

            case 80:
                tool = new Oven();
                //_cookManager.RegisterCookingTool(id, tool as CookingTool);
                break;

            case 90:
                tool = new TrashCan();
                break;

            default:
                tool = null;
                break;
        }

        if (tool == null)
        {
            entityId = 0;
            return null;
        }

        entityId = GenerateEntityId();
        tool.InitToolId(id);

        RegisterEntity(entityId, tool);

        return tool;
    }

    //public void CombineEntity(long resultId, long removeId, Entity entity)
    //{
    //    _entities.Remove(removeId);

    //    _entities[resultId] = entity;
    //}

    //public void UpdateEntity(long entityId, Entity entity)
    //{
    //    _entities[entityId] = entity;
    //}

    private long GenerateEntityId()
    {
        long newId = Interlocked.Increment(ref _nextEntityId);

        return newId;
    }

    /// <summary>
    /// Room 접속
    /// <para/> 사용시 PushJob안에 넣어줘야 함
    /// </summary>
    public void Enter(Player player)
    {
        _players.Add(player);
        player.Room = this;
        _playerCnt++;
    }

    /// <summary>
    /// Room 접속 해제
    /// <para/> 사용시 PushJob안에 넣어줘야 함
    /// </summary>
    public void Leave(Player player)
    {
        _players.Remove(player);
        player.Room = null;

        if (player.Parent is CookingTool cookingTool)
        {
            cookingTool.RemovePlayer(player);
        }

        if (--_playerCnt <= 0)
        {
            ServerContext.Instance.RoomManager.RemoveRoom(Id);
        }

        PlayerLeavePacket packet = new()
        {
            PlayerID = player.PlayerId
        };

        BroadCast(PacketSerializer.Serialize(packet, true));
    }

    public void BroadCastForce(byte[] packet)
    {
        foreach (var player in _players)
        {
            player.Send(packet);
        }
    }

    public void BroadCast(byte[] packet)
    {
        foreach (var player in _players)
        {
            player.AddBatch(packet);
        }
    }
    
    public void Notificate(string message)
    {
        PushJob(() =>
        {
            RoomNotificationPacket packet = new()
            {
                Message = message
            };

            BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    public bool InteractEntity(long entityId, Player player)
    {
        if (!_entities.TryGetValue(entityId, out var entity) || entity.IsDestroyed) return false;
        if (player.HoldingEntity != null && entityId == player.HoldingEntity.EntityId) return false;
        if (entity is not IInteractable interactable) return false;

        return interactable.Interact(player);
    }

    public bool InsertEntity(long targetId, long subjectId)
    {
        if (!_entities.TryGetValue(targetId, out var target) || target.IsDestroyed) return false;
        if (!_entities.TryGetValue(subjectId, out var subject) || subject.IsDestroyed) return false;

        if (target is not ContainerTool containerTool) return false;

        return containerTool.Insert(subject);
    }

    public bool EnterToolPlayer(long toolId, Player player)
    {
        if (!_entities.TryGetValue(toolId, out var entity) || entity.IsDestroyed) return false;

        if (entity is Oven || entity is Pot || entity is TrashCan)
        {
            return (entity as ContainerTool).AddPlayer(player);
        }

        return false;
    }

    public bool ExitToolPlayer(long toolId, Player player)
    {
        if (!_entities.TryGetValue(toolId, out var entity) || entity.IsDestroyed) return false;

        if (entity is Oven || entity is Pot || entity is TrashCan)
        {
            return (entity as ContainerTool).RemovePlayer(player);
        }

        return false;
    }

    public bool ForceEjectTool(long toolId)
    {
        if (!_entities.TryGetValue(toolId, out var entity) || entity.IsDestroyed) return false;

        if (entity is Pot pot)
        {
            pot.ForceEject();

            return true;
        }

        return false;
    }

    public bool EnterJumpPad(long padId)
    {
        if (!_entities.TryGetValue(padId, out var entity) || entity.IsDestroyed) return false;
        if (entity is not JumpPad jumpPad) return false;

        return jumpPad.HandlePlayerEnter();
    }

    public bool ExitJumpPad(long padId)
    {
        if (!_entities.TryGetValue(padId, out var entity) || entity.IsDestroyed) return false;
        if (entity is not JumpPad jumpPad) return false;

        return jumpPad.HandlePlayerExit();
    }

    //public bool LeaveStove(long panId)
    //{
    //    if (!_entities.TryGetValue(panId, out var entity) || entity.IsDestroyed) return false;
    //    if (entity is not Pan pan) return false;

    //    return pan.LeaveStove();
    //}

    public void InteractDoor(DoorId doorId, string playerId)
    {
        _doors[doorId].BeginInteract(playerId);
    }

    public void StopInteractDoor(DoorId doorId, string playerId)
    {
        _doors[doorId].EndInteract(playerId);
    }

    public void AddPendingMoney(int amount)
    {
        if (amount <= 0) return;

        _pendingMoney += amount;

        BroadCastPendingMoney();
    }

    public bool TrySpendMoney(int cost)
    {
        if (cost < 0) return false;
        if (_money < cost) return false;

        _money -= cost;

        BroadCastMoney();

        return true;
    }

    private void BroadCastMoney()
    {
        RoomMoneyPacket packet = new()
        {
            Money = _money
        };

        BroadCast(PacketSerializer.Serialize(packet, true));
    }

    private void BroadCastPendingMoney()
    {
        RoomPendingMoneyPacket packet = new()
        {
            Money = _pendingMoney
        };

        BroadCast(PacketSerializer.Serialize(packet, true));
    }

    public bool ServeDish(long dishId)
    {
        if (!Entities.TryGetValue(dishId, out var entity) || entity.IsDestroyed) return false;
        if (entity is not Dish dish || dish.Ingredient == null) return false;

        _dishManager.SubmitDish(dish.Ingredient.IngredientId);

        dish.Destroy();
        dish.Parent = null;

        DishEjectPacket packet = new()
        {
            DishEntityId = dish.EntityId,
            EjectSeed = (int)TimeUtil.NowMs()
        };

        BroadCast(PacketSerializer.Serialize(packet, true));

        return true;
    }

    private void RegisterEntity(long id, Entity entity)
    {
        entity.InitEntityId(id);
        entity.AttachRoom(this);
        _entities[id] = entity;
    }

    public void UnregisterEntity(long id)
    {
        _entities.Remove(id); // GC 효율보면서 필요시 Pool로 반환
    }

    private void FlushSend()
    {
        foreach (var player in _players)
        {
            player.Flush();
        }
    }

    private PlayerMovementPacket GetMovementData(Player player)
    {
        PlayerMovementPacket packet = new()
        {
            PlayerId = player.PlayerId,
            Position = player.Position,
            Rotation = player.Rotation,
            CombinedState = player.State
        };

        return packet;
    }

    private PingResultPacket GetPingData(Player player)
    {
        PingResultPacket packet = new()
        {
            PlayerId = player.PlayerId,
            Ping = player.Ping
        };

        return packet;
    }

    private void HandleDoorOpened(Door door)
    {
        foreach (var d in _doors.Values)
        {
            d.IsOpen = false;
        }

        door.IsOpen = true;

        OpenDoorPacket packet = new()
        {
            DoorId = door.DoorId
        };

        if (door.DoorId == DoorId.Kitchen)
        {
            Start();
        }

        BroadCast(PacketSerializer.Serialize(packet, true));
    }

    /// <summary>
    /// 스테이지 종료 (타임아웃 또는 클라 요청)
    /// <para/> 임시 보유량을 보유량에 정산하고 결과를 전송
    /// </summary>
    public void EndStage()
    {
        bool isSuccess = _dishManager.IsStageSuccess();
        int pendingMoney = _pendingMoney;

        Stop();

        _money += pendingMoney;
        _pendingMoney = 0;

        StageStopPacket packet = new()
        {
            PendingMoney = pendingMoney,
            Money = _money,
            IsSuccess = isSuccess
        };

        BroadCast(PacketSerializer.Serialize(packet, true));
    }
}
