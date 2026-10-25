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
    public PlayerCombinedState State
    {
        get => _state;
        set
        {
            if (_state.Equals(value)) return;

            _state = value;
            MakeDirty(DirtyMask.Transform);
        }
    }
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
    public Quaternion Rotation
    {
        get => _rotation;
        set
        {
            if (_rotation == value) return;

            _rotation = value;
            MakeDirty(DirtyMask.Transform);
        }
    }

    public const int MaxHp = 100;

    private PlayerCombinedState _state;
    private int _hp = MaxHp;
    private float _ping;
    private Vector3 _position;
    private Quaternion _rotation;

    private PacketBatch _batch = new();

    public ValueTask Send(ReadOnlyMemory<byte> packet)
    {
        return LatencySimulator.SendAsync(Session, packet);
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

    // 리스폰: 소유/부모 정보를 초기화하고 클라의 고정 스폰포인트로 되돌리도록 패킷 전송
    public void Respawn()
    {
        Entity? heldEntity = HoldingEntity;

        if (heldEntity != null)
        {
            heldEntity.Parent = null;
        }

        Parent = null;
        Hp = MaxHp;

        if (Room == null) return;

        PlayerRespawnPacket packet = new()
        {
            PlayerID = PlayerId
        };

        Room.BroadCast(PacketSerializer.Serialize(packet, true));
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
            var state = State;

            packet.Players.Add(new()
            {
                PlayerId = PlayerId,
                Position = Position,
                Rotation = Rotation,
                CombinedState = state
            });

            if (state.Interaction != PlayerInteraction.None)
            {
                _state = new(
                    state.PhysicalMode,
                    state.MoveDir,
                    state.IsRun,
                    PlayerInteraction.None,
                    state.HeldObjType);
            }
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
