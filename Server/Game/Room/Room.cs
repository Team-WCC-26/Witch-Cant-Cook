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

    public IReadOnlyDictionary<long, Entity> Entityes => _entities;
    private readonly Dictionary<long, Entity> _entities = new();

    public int PlayerCnt => _playerCnt;
    private int _playerCnt = 0;
    private int _tick = 0;

    private long _nextEntityId = 0;

    private JobQueue _jobQueue = new();
    private Shard _shard;

    private readonly Dictionary<DoorId, Door> _doors = new()
    {
        [DoorId.Lobby] = new(2, 3000),
        [DoorId.Kitchen] = new(2, 3000)
    };

    public Room(string id, string name, string password)
    {
        Id = id;
        Name = name;
        Password = password;
    }

    public void Tick(long deltaTime)
    {
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

            player.Send(PacketSerializer.Serialize(packet, true));
        }

        // Open Door
        foreach (var doorPair in _doors)
        {
            if (doorPair.Value.Tick())
            {
                foreach (var door in _doors.Values)
                {
                    door.IsOpen = false;
                }

                doorPair.Value.IsOpen = true;

                PushJob(() =>
                {
                    OpenDoorPacket packet = new()
                    {
                        DoorId = doorPair.Key
                    };

                    BroadCast(PacketSerializer.Serialize(packet, true));
                });

                break;
            }
        }

        _tick++;
    }

    public bool IsEnable()
    {
        return _playerCnt >= 0 && _playerCnt < MaxPlayerCount;
    }

    public void PushJob(Action job) => _jobQueue.Push(job);

    public void InitShard(Shard shard)
    {
        _shard = shard;
        _jobQueue.InitShard(shard);
    }

    public Shard GetShard()
    {
        return _shard;
    }

    public Ingredient GenerateIngredient(int id)
    {
        Ingredient ingredient = new(GenerateEntityId(), id);

        _entities[ingredient.EntityId] = ingredient;

        return ingredient;
    }

    public Tool GenerateTool(int id)
    {
        Tool tool = new(GenerateEntityId(), id);

        _entities[tool.EntityId] = tool;

        return tool;
    }

    public void DestroyIngredient(long id)
    {
        _entities.Remove(id);
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

    public void BroadCast(byte[] packet)
    {
        foreach (var player in _players)
        {
            player.Send(packet);
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

            BroadCast(PacketSerializer.Serialize(packet));
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
}
