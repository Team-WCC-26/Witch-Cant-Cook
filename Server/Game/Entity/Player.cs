using Protocol;
using SuperSocket.Server.Abstractions.Session;
using System.Numerics;

namespace Server;

/// <summary>
/// 플레이어 연결 상태 (세션 레코드 상태와 별개로 Room 동기화/작업 펜싱에 사용된다)
/// </summary>
public enum PlayerConnectionState
{
    Live,        // 정상 증분 동기화 중
    Detached,    // 연결 끊김 (유예 시간 동안 보존, 전송 대상 아님)
    Syncing,     // 재접속 바인딩 완료, 스냅샷 전송 중
    AwaitingAck, // 스냅샷 전송 완료, ACK 대기 (ACK 전 증분은 pending 배치에 보관)
}

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

    // 연결 상태 (재접속/유예 판정과 전송 분기에 사용, 크로스 스레드에서 읽힌다)
    public volatile PlayerConnectionState ConnectionState = PlayerConnectionState.Live;

    public const int MaxHp = 100;

    private PlayerCombinedState _state;
    private int _hp = MaxHp;
    private float _ping;
    private Vector3 _position;
    private Quaternion _rotation;

    private PacketBatch _batch = new();        // 라이브 증분 배치
    private PacketBatch _pendingBatch = new(); // 재접속 ACK 전 증분 보관 배치

    // 배치 보관 동기화: 상태 인지 전송(네트워크 스레드)과 샤드 작업(AddBatch/Flush)의 경합을 막는다
    private readonly object _batchLock = new();

    // 일반 전송: 연결 상태에 따라 직접 전송하거나 pending 배치에 보관/폐기한다 (ACK 게이팅 준수)
    public ValueTask Send(ReadOnlyMemory<byte> packet)
    {
        lock (_batchLock)
        {
            switch (ConnectionState)
            {
                case PlayerConnectionState.Live:
                    break;

                case PlayerConnectionState.Syncing:
                case PlayerConnectionState.AwaitingAck:
                    // 스냅샷 ACK 전 패킷은 pending 배치에 보관했다가 ACK 후 일괄 전송한다
                    _pendingBatch.Add(packet);
                    return ValueTask.CompletedTask;

                default:
                    // 연결 끊김 등: 전송 대상이 아니므로 폐기한다
                    return ValueTask.CompletedTask;
            }
        }

        return LatencySimulator.SendAsync(Session, packet);
    }

    // 제어 전송: 하트비트/세션 협상/스냅샷 청크 등 연결 상태와 무관하게 즉시 전송해야 하는 패킷용
    public ValueTask SendControl(ReadOnlyMemory<byte> packet)
    {
        return LatencySimulator.SendAsync(Session, packet);
    }

    // 세대 펜싱 게임플레이 작업 푸시 (Player.Room 대상): 패킷을 보낸 수신 세션과 세대를 캡처해
    // 실행 시점에 (player.Session == 수신 세션 && 세대 일치 && Live) 재검증한다
    // (mutable한 Player.Session이 아니라 수신 세션 기준이므로 재접속 경합 중 오래된 작업이 새 세대로 통과하지 못한다)
    public void PushGameplayJob(Session session, Action<Room> job)
    {
        Room? room = Room;

        if (room == null) return;

        PushGameplayJob(session, room, job);
    }

    // 세대 펜싱 게임플레이 작업 푸시 (대상 룸을 직접 지정: 룸 참가/퇴장 요청 등)
    public void PushGameplayJob(Session session, Room room, Action<Room> job)
    {
        PushGameplayJob(session, room, job, null);
    }

    // 세대 펜싱 게임플레이 작업 푸시 (폐기 시 롤백 지정): 펜싱 실패로 폐기되면 rollback을 실행한다
    // (생성 직후 참가가 폐기되는 경우 생성된 빈 방을 회수하는 등 확정 전 자원을 정리한다)
    public void PushGameplayJob(Session session, Room room, Action<Room> job, Action? rollback)
    {
        int generation = session.BindingGeneration;

        room.PushJob(() =>
        {
            // 실행 시점에 수신 세션이 여전히 현재 바인딩 세션인지 재검증한다
            // (재접속으로 세션이 교체됐거나 세대가 어긋난 오래된 작업은 조용히 폐기한다)
            if (ConnectionState != PlayerConnectionState.Live
                || !ReferenceEquals(Session, session)
                || session.BindingGeneration != generation)
            {
                rollback?.Invoke();
                return;
            }

            job(room);
        });
    }

    public void ApplyDamage(int damage)
    {
        if (damage <= 0) return;

        Hp -= damage;

        // HP가 0이 되면 부활시킨다 (스폰 지점으로 복귀)
        if (Hp == 0) Respawn();
    }

    // 즉사 처리: HP를 0으로 만들고 부활시킨다 (오븐 조리 완료 등)
    public void Kill()
    {
        Hp = 0;

        Respawn();
    }

    // 룸 퇴장: 펜싱 경로로 통일한다 (요청 시점의 Room을 캡처해 실행 시점 재참조를 막는다)
    public void LeaveRoom(Session session)
    {
        Room? room = Room;

        if (room == null) return;

        PushGameplayJob(session, room, targetRoom => targetRoom.Leave(this));
    }

    // 리스폰: 소유/부모 정보를 초기화하고 클라의 고정 스폰포인트로 되돌리도록 패킷 전송
    public void Respawn()
    {
        Entity? heldEntity = HoldingEntity;

        if (heldEntity != null)
        {
            heldEntity.Parent = null;
        }

        // 컨테이너 도구(솥/오븐/쓰레기통) 안에 있으면 멤버십부터 정리한다 (잔류 참조 방지)
        if (Parent is ContainerTool container)
        {
            container.RemovePlayer(this);
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
        lock (_batchLock)
        {
            // Detached: 전송 대상이 아니므로 증분을 폐기한다
            if (ConnectionState == PlayerConnectionState.Detached) return;

            // Syncing/AwaitingAck: ACK 전 증분은 pending 배치에 보관했다가 ACK 후 일괄 전송한다
            if (ConnectionState is PlayerConnectionState.Syncing or PlayerConnectionState.AwaitingAck)
            {
                _pendingBatch.Add(packet);
                return;
            }

            _batch.Add(packet);
        }
    }

    public void Flush()
    {
        // 라이브 상태에서만 라이브 배치를 전송한다 (Syncing 중 누적분은 ACK 후 FlushPending으로 전송)
        if (ConnectionState != PlayerConnectionState.Live) return;

        ReadOnlyMemory<byte> sendBuffer;

        lock (_batchLock)
        {
            sendBuffer = _batch.Build();
            _batch = new();
        }

        if (sendBuffer.IsEmpty) return;

        Send(sendBuffer);
    }

    // 재접속 ACK 이후 pending 배치의 누적 증분을 전송한다 (샤드 작업 안에서 호출)
    public void FlushPending()
    {
        // Live 전환 직후의 잔량 전송이므로 Live가 아니면 폐기한다 (종료 경합 방어)
        if (ConnectionState != PlayerConnectionState.Live) return;

        ReadOnlyMemory<byte> sendBuffer;

        lock (_batchLock)
        {
            sendBuffer = _pendingBatch.Build();
            _pendingBatch = new();
        }

        if (sendBuffer.IsEmpty) return;

        Send(sendBuffer);
    }

    // 재접속 바인딩/퇴장 시 배치를 초기화한다 (미전송 증분 잔량을 제거, 샤드 작업 안에서 호출)
    public void ResetBatches()
    {
        lock (_batchLock)
        {
            _batch.Clear();
            _pendingBatch.Clear();
        }
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

                MakeDirty(DirtyMask.Transform);
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
