using Protocol;
using System.Net.Sockets;
using System.Numerics;

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

    private long _nextEntityId = 0;

    private Shard _shard;

    private readonly Dictionary<DoorId, Door> _doors = new();

    private TimerManager _timerManager = new();
    private IngredientSpawner _ingredientSpanwer;
    private DishManager _dishManager;

    public Room(string id, string name, string password)
    {
        Id = id;
        Name = name;
        Password = password;

        _ingredientSpanwer = new(this, _timerManager, 3);
        _dishManager = new(this, _timerManager);
    }

    public void Init()
    {
        _players.Clear();
        _playerCnt = 0;
        _tick = 0;
        _nextEntityId = 0;
        _doors.Clear();

        _doors[DoorId.Lobby] = new(DoorId.Lobby, 2, 3, _timerManager, HandleDoorOpened);
        _doors[DoorId.Kitchen] = new(DoorId.Kitchen, 2, 3, _timerManager, HandleDoorOpened);

        _ingredientSpanwer.SetStage(1);
    }

    public void Start()
    {
        _dishManager.Start();
        _ingredientSpanwer.Start();
    }

    public void Stop()
    {
        _dishManager.Stop();
        _ingredientSpanwer.Stop();
    }

    public void Tick(long deltaTime)
    {
        _timerManager.Tick(deltaTime);

        WorldStatePacket packet = new()
        {
            Tick = _tick
        };

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

    public void PushJob(Action job) => _shard.Push(job);

    public void InitShard(Shard shard)
    {
        _shard = shard;
    }

    public Shard GetShard()
    {
        return _shard;
    }

    public void MakeDirty(Entity entity)
    {
        _dirtyEntities.Add(entity);
    }

    public Ingredient GenerateIngredient(int id, out long entityId)
    {
        Ingredient ingredient = new();
        entityId = GenerateEntityId();
        ingredient.InitIngredientId(id);

        RegisterEntity(entityId, ingredient);

        return ingredient;
    }

    public Tool GenerateTool(int id, out long entityId)
    {
        Tool tool;
        
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

            case 80:
                tool = new Oven();
                //_cookManager.RegisterCookingTool(id, tool as CookingTool);
                break;

            default:
                tool = null;
                break;
        }
        
        entityId = GenerateEntityId();
        tool.InitToolId(id);

        RegisterEntity(entityId, tool);

        return tool;
    }

    public void DestroyIngredient(long id)
    {
        _entities.Remove(id);
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
        if (!_entities.TryGetValue(subjectId, out var subject) || target.IsDestroyed) return false;

        if (target is not ContainerTool containerTool) return false;

        return containerTool.Insert(subject);
    }

    public void InteractDoor(DoorId doorId, string playerId)
    {
        _doors[doorId].BeginInteract(playerId);
    }

    public void StopInteractDoor(DoorId doorId, string playerId)
    {
        _doors[doorId].EndInteract(playerId);
    }

    public bool ServeDish(long dishId)
    {
        if (!Entities.TryGetValue(dishId, out var entity)) return false;
        if (entity is not Dish dish || dish.Ingredient == null) return false;

        var ingredient = dish.Ingredient;
        _dishManager.SubmitDish(new(ingredient.IngredientId, ingredient.ProcessState));

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
}
