using Protocol;

namespace Server;

public class JumpPad : Tool
{
    private const long ChargeTimeMs = 1000;
    private const long ReturnTimeMs = 100;

    public JumpPadChargeState State => _state;
    private JumpPadChargeState _state = JumpPadChargeState.Idle;

    // 탑승자 목록은 유지하지 않고 진입/이탈 패킷 기준 인원만 센다
    private int _riderCount = 0;

    // 발사 회차 (클라이언트 중복 이벤트 방지용)
    private int _fireRound = 0;

    private TimerHandle? _chargeTimer;
    private TimerHandle? _returnTimer;

    // 점프대 진입은 클라이언트 트리거 패킷으로만 처리한다
    public override bool Interact(Player player) => false;

    public bool HandlePlayerEnter()
    {
        _riderCount++;

        if (_state != JumpPadChargeState.Idle || _riderCount != 1) return false;

        StartCharge();

        return true;
    }

    public bool HandlePlayerExit()
    {
        if (_riderCount <= 0) return false;

        _riderCount--;

        if (_state != JumpPadChargeState.Charging || _riderCount > 0) return false;

        CancelCharge();

        return true;
    }

    private void StartCharge()
    {
        _state = JumpPadChargeState.Charging;
        _chargeTimer = Room.TimerManager.Schedule(ChargeTimeMs, this, static t => t.Fire());
    }

    private void CancelCharge()
    {
        if (_chargeTimer != null)
        {
            Room.TimerManager.Cancel(_chargeTimer.Value);
            _chargeTimer = null;
        }

        _state = JumpPadChargeState.Return;
        _returnTimer = Room.TimerManager.Schedule(ReturnTimeMs, this, static t => t.CompleteReturn());

        BroadCastEvent(JumpPadEventType.Cancel);
    }

    private void Fire()
    {
        _chargeTimer = null;
        _fireRound++;

        _state = JumpPadChargeState.Return;
        _returnTimer = Room.TimerManager.Schedule(ReturnTimeMs, this, static t => t.CompleteReturn());

        BroadCastEvent(JumpPadEventType.Fire);
    }

    private void CompleteReturn()
    {
        _returnTimer = null;
        _state = JumpPadChargeState.Idle;

        if (_riderCount > 0)
        {
            StartCharge();
        }

        BroadCastEvent(JumpPadEventType.Return);
    }

    private void BroadCastEvent(JumpPadEventType eventType)
    {
        JumpPadEventPacket packet = new()
        {
            PadEntityId = EntityId,
            EventType = eventType,
            FireRound = _fireRound,
        };

        Room.BroadCast(PacketSerializer.Serialize(packet, true));
    }

    // 로비 복귀 등 도구를 초기 상태로 되돌린다 (ClearKitchenEntities에서 호출)
    public override void Reset()
    {
        base.Reset();

        if (_chargeTimer != null)
        {
            Room.TimerManager.Cancel(_chargeTimer.Value);
            _chargeTimer = null;
        }

        if (_returnTimer != null)
        {
            Room.TimerManager.Cancel(_returnTimer.Value);
            _returnTimer = null;
        }

        _state = JumpPadChargeState.Idle;
        _riderCount = 0;
    }

    // 재접속 스냅샷 수집용: 현재 장전 상태/탑승자/발사 회차/타이머 남은 시간을 조회한다
    public JumpPadSnapshot GetSnapshot()
    {
        return new JumpPadSnapshot()
        {
            State = _state,
            RiderCount = _riderCount,
            FireRound = _fireRound,
            ChargeRemainingMs = GetTimerRemainingMs(_chargeTimer),
            ReturnRemainingMs = GetTimerRemainingMs(_returnTimer)
        };
    }

    // 유효한 타이머의 남은 시간 조회 (유효하지 않으면 0)
    private long GetTimerRemainingMs(TimerHandle? handle)
    {
        if (handle == null || !Room.TimerManager.CheckHandleValid(handle.Value)) return 0;

        return Room.TimerManager.RemainingTime(handle.Value);
    }
}
