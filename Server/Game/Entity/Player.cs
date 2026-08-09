using Protocol;
using SuperSocket.Server.Abstractions.Session;
using System.Numerics;

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
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }

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

    public override void WriteSnapShot(PacketBatch batch, DirtyMask mask)
    {
        if (mask.HasFlag(DirtyMask.Position))
        {

        }

        if (mask.HasFlag(DirtyMask.Rotation))
        {

        }
    }
}
