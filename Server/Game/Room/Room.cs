using Protocol;

namespace Server;

public class Room
{
    public int RoomId { get; }
    private readonly List<Player> _players = new();
    public readonly int MaxPlayerCount = 2;
    private int _playerCnt = 0;

    private JobQueue _jobQueue = new();
    private Shard _shard;

    public Room(int id)
    {
        RoomId = id;
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
}
