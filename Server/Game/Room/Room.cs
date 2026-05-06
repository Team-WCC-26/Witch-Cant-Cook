using Protocol;

namespace Server;

public class Room
{
    public string Id { get; }
    public string Name { get; }
    public string Password { get; }

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
        return _playerCnt >= 0 && _playerCnt <= MaxPlayerCount;
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

    public void Enter(Player player)
    {
        PushJob(() =>
        {
            _players.Add(player);
            player.Room = this;
            _playerCnt++;
        });
    }

    public void Leave(Player player)
    {
        PushJob(() =>
        {
            _players.Remove(player);
            player.Room = null;
            _playerCnt--;
        });
    }

    public void BroadCast(byte[] packet)
    {
        PushJob(() =>
        {
            foreach (var player in _players)
            {
                player.Send(packet);
            }
        });
    }
    
    public void Notificate(string message)
    {
        RoomNotificationPacket packet = new()
        {
            Message = message
        };

        BroadCast(PacketSerializer.Serialize(packet));
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
