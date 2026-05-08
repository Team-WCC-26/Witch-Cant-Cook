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

    public int PlayerCnt => _playerCnt;
    private int _playerCnt = 0;
    private int _tick = 0;

    private JobQueue _jobQueue = new();
    private Shard _shard;

    public Room(string id, string name, string password)
    {
        Id = id;
        Name = name;
        Password = password;
    }

    public void Tick()
    {
        foreach (var player in _players)
        {
            WorldStatePacket packet = new()
            {
                Tick = _tick
            };
            packet.Players.Add(ToData(player));

            foreach (var p in _players)
            {
                if (p == player) continue;

                packet.Players.Add(ToData(p));
            }

            player.Send(PacketSerializer.Serialize(packet, true));
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

    private PlayerMovementPacket ToData(Player player)
    {
        PlayerMovementPacket packet = new()
        {
            PlayerId = player.PlayerId,
            Position = player.Pos,
            Rotation = player.Rot,
            CombinedState = player.State
        };

        return packet;
    }
}
