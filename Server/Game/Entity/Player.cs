using Protocol;
using SuperSocket.Server.Abstractions.Session;

namespace Server;

public class Player : Entity
{
    public string PlayerId { get; set; }
    public IAppSession Session { get; set; }
    public float LastPingTime { get; set; }
    public float Ping { get; set; }
    public Room? Room { get; set; }
    public PlayerCombinedState State { get; set; }
    public Entity? HoldingEntity { get; set; }

    private PacketBatch _batch = new();

    public ValueTask Send(ReadOnlyMemory<byte> packet)
    {
        return Session.SendAsync(packet);
    }

    public void LeaveRoom()
    {
        Room?.PushJob(() =>
        {
            Room?.Leave(this);
        });
    }

    public void AddBatch(ReadOnlyMemory<byte> packet)
    {
        _batch.Add(packet);
    }

    public void Flush()
    {
        var sendBuffer = _batch.Build();

        if (sendBuffer.IsEmpty) return;

        _batch = new();

        Send(sendBuffer);
    }
}
