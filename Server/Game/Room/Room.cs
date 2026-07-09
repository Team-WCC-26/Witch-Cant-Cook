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

    public int PlayerCnt => _playerCnt;
    private int _playerCnt = 0;
    private int _tick = 0;

    private long _nextEntityId = 0;

    private Shard _shard;

    private readonly Dictionary<DoorId, Door> _doors = new();

    private TimerManager _timerManager = new();
    private CookManager _cookManager = new();

    public Room(string id, string name, string password)
    {
        Id = id;
        Name = name;
        Password = password;
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
    }

    public void Tick(long deltaTime)
    {
        _cookManager.Tick(deltaTime);
        _timerManager.Tick(deltaTime);

        foreach (var player in _players)
        {
            WorldStatePacket packet = new()
            {
                Tick = _tick
            };
            packet.Players.Add(GetMovementData(player));

            foreach (var p in _players)
            {
                if (p == player) continue;

                packet.Pings.Add(GetPingData(p));
                packet.Players.Add(GetMovementData(p));
                // Room의 재료 업데이트 상태 보내기
            }

            player.AddBatch(PacketSerializer.Serialize(packet, true));
        }

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

    public Ingredient GenerateIngredient(int id, out long entityId)
    {
        Ingredient ingredient = new(id);
        entityId = GenerateEntityId();

        _entities[entityId] = ingredient;

        return ingredient;
    }

    public Tool GenerateTool(int id, out long entityId)
    {
        Tool tool;
        
        switch (id)
        {
            case 20:
            case 40:
            case 80:
                tool = new CookingTool(id);
                _cookManager.RegisterCookingTool(id, tool as CookingTool);
                break;

            case 50:
                tool = new Dish(id);
                break;

            default:
                tool = new(id);
                break;
        }
        
        entityId = GenerateEntityId();

        _entities[entityId] = tool;

        return tool;
    }

    public void DestroyIngredient(long id)
    {
        _entities.Remove(id);
    }

    public void CombineEntity(long resultId, long removeId, Entity eentity)
    {
        _entities.Remove(removeId);

        _entities[resultId] = eentity;
    }

    public void UpdateEntity(long entityId, Entity entity)
    {
        _entities[entityId] = entity;
    }

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

    public void InteractDoor(DoorId doorId, string playerId)
    {
        _doors[doorId].BeginInteract(playerId);
    }

    public void StopInteractDoor(DoorId doorId, string playerId)
    {
        _doors[doorId].EndInteract(playerId);
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

        BroadCast(PacketSerializer.Serialize(packet, true));
    }
}
