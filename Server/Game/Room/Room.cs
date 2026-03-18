using Protocol;

namespace Server;

public class Room
{
    private Queue<Action> _jobs = new(); // TODO => Global Job + Shard 시스템으로 넘겨줘야 함
    private bool _isRunning = false;

    public int RoomId { get; }
    public bool IsEnable => _players.Count >= 0 && _players.Count <= MaxPlayerCount;

    public readonly int MaxPlayerCount = 2;
    private readonly List<Player> _players = new();

    public Room(int id)
    {
        RoomId = id;
    }

    public void Push(Action job)
    {
        lock (_jobs)
        {
            _jobs.Enqueue(job);

            if (_isRunning) return;

            _isRunning = true;
        }
        
    }

    private void Process()
    {
        while (true)
        {
            Action job;

            lock (_jobs)
            {
                if (_jobs.Count == 0)
                {
                    _isRunning = false;
                    return;
                }

                job = _jobs.Dequeue();
            }

            job.Invoke();
        }
    }

    public void Enter(Player player)
    {
        _players.Add(player);
        player.Room = this;
    }

    public void Leave(Player player)
    {
        _players.Remove(player);
        player.Room = null;
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
        RoomNotificationPacket packet = new()
        {
            Message = message
        };

        BroadCast(PacketSerializer.Serialize(packet));
    }
}
