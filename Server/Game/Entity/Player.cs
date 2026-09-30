using Protocol;
using SuperSocket.Server.Abstractions.Session;
using System.Numerics;

namespace Server;

public class Player : Entity
{
    public string PlayerId { get; set; }
    public IAppSession Session { get; set; }
    public float LastPingTime { get; set; }
    public float Ping 
    {
        get => _ping;
        set
        {
            _ping = value;
            MakeDirty(DirtyMask.Ping);
        }
    }
    public Room? Room { get; set; }
    public PlayerCombinedState State { get; set; }
    public Entity? HoldingEntity { get; set; }
    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_position == value) return;

            _position = value;
            MakeDirty(DirtyMask.Transform);
        }
    }
    public int Hp
    {
        get => _hp;
        set
        {
            value = Math.Max(0, value);

            if (_hp == value) return;

            _hp = value;
            MakeDirty(DirtyMask.Hp);
        }
    }
    public Quaternion Rotation { get; set; }

    public const int MaxHp = 100;

    private int _hp = MaxHp;
    private float _ping;
    private Vector3 _position;

    private PacketBatch _batch = new();

    public ValueTask Send(ReadOnlyMemory<byte> packet)
    {
        return Session.SendAsync(packet);
    }

    public void ApplyDamage(int damage)
    {
        if (damage <= 0) return;

        Hp -= damage;
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

    public override void WriteSnapShot(WorldStatePacket packet, DirtyMask mask)
    {
        base.WriteSnapShot(packet, mask);

        if (mask.HasFlag(DirtyMask.Transform))
        {
            packet.Players.Add(new()
            {
                PlayerId = PlayerId,
                Position = Position,
                Rotation = Rotation,
                CombinedState = State
            });
        }

        if (mask.HasFlag(DirtyMask.Ping))
        {
            packet.Pings.Add(new()
            {
                PlayerId = PlayerId,
                Ping = Ping
            });
        }

        if (mask.HasFlag(DirtyMask.Hp))
        {
            packet.PlayerHps.Add(new()
            {
                PlayerId = PlayerId,
                Hp = Hp
            });
        }
    }
}
