using Protocol;

namespace Server;

// 룸 고정 오브젝트: 펀치로 퇴장 의사를 확인하고 활성화 상태에서 재펀치하면 펀치한 플레이어를 퇴장시키는 트리거
// (도구가 아닌 룸 수명 엔티티이므로 스테이지별 생성/정리 대상이 아니다)
public class LeaveRoomTrigger : Entity, IInteractable, IFixedTool
{
    private const long RevertDelayMs = 10_000;

    public LeaveRoomTriggerState State => _state;
    private LeaveRoomTriggerState _state = LeaveRoomTriggerState.Idle;

    private TimerHandle? _revertTimer;

    public bool Interact(Player player)
    {
        // 상호작용 시점에 룸에 남아 있는 플레이어만 처리한다 (퇴장/재접속 경합 방어)
        if (!Room.Players.Contains(player)) return false;

        // 스테이지 진행 중에는 퇴장 트리거를 건드릴 수 없다 (로비/정산 페이즈만 허용)
        if (Room.IsStageRunning) return false;

        if (_state == LeaveRoomTriggerState.Active)
        {
            Eject(player);

            return true;
        }

        Activate();

        return true;
    }

    private void Activate()
    {
        _state = LeaveRoomTriggerState.Active;
        _revertTimer = Room.TimerManager.Schedule(RevertDelayMs, this, static t => t.Revert());

        BroadCastState(null);
    }

    private void Revert()
    {
        _revertTimer = null;
        _state = LeaveRoomTriggerState.Idle;

        BroadCastState(null);
    }

    // 활성화 상태에서 펀치한 플레이어에게 퇴장을 통보한다 (활성화한 플레이어가 아니어도 퇴장)
    private void Eject(Player player)
    {
        if (_revertTimer != null)
        {
            Room.TimerManager.Cancel(_revertTimer.Value);
            _revertTimer = null;
        }

        _state = LeaveRoomTriggerState.Idle;

        var statePacket = PacketSerializer.Serialize(CreateStatePacket(player.PlayerId), true);
        Room.BroadCast(statePacket);

        // 퇴장자는 Room.Leave 후 이 룸의 Flush 대상에서 빠지므로 잔여 배치는 어디에서도 전송되지 않는다
        // (새 룸 참가 시 엔티티 id 1이 겹쳐 남은 배치가 새 룸 상태로 오적용된다)
        // 배치를 먼저 비운 뒤 같은 상태 패킷을 직접 전송해 퇴장자에게 확실하게 전달한다
        // (순서가 반대면 Syncing/AwaitingAck 상태에서 직접 전송분이 pending 배치에 들어가 같이 지워진다)
        player.ResetBatches();
        player.Send(statePacket);

        // 명시적 룸 퇴장 경로와 동일하게 정리한다: 쥔 Entity를 떨어뜨린 뒤 룸 멤버십에서 제거한다
        if (player.HoldingEntity != null)
        {
            player.HoldingEntity.Parent = null;
        }

        Room.Leave(player);
    }

    // 스테이지 시작/종료/룸 정지 시 대기 상태로 되돌린다 (Room.ResetLeaveRoomTrigger에서 호출)
    public bool Reset()
    {
        if (_revertTimer != null)
        {
            Room.TimerManager.Cancel(_revertTimer.Value);
            _revertTimer = null;
        }

        if (_state == LeaveRoomTriggerState.Idle) return false;

        _state = LeaveRoomTriggerState.Idle;

        return true;
    }

    // 재접속 스냅샷 수집용: 공유 상태와 복귀 타이머 남은 시간을 조회한다
    public LeaveRoomTriggerSnapshot GetSnapshot()
    {
        return new LeaveRoomTriggerSnapshot()
        {
            State = _state,
            RevertRemainingMs = GetRevertRemainingMs()
        };
    }

    public LeaveRoomTriggerStatePacket CreateStatePacket(string? ejectedPlayerId)
    {
        return new LeaveRoomTriggerStatePacket()
        {
            TriggerEntityId = EntityId,
            State = _state,
            RemainingMs = GetRevertRemainingMs(),
            EjectedPlayerId = ejectedPlayerId
        };
    }

    // 유효한 타이머의 남은 시간 조회 (유효하지 않으면 0)
    private long GetRevertRemainingMs()
    {
        if (_revertTimer == null || !Room.TimerManager.CheckHandleValid(_revertTimer.Value)) return 0;

        return Room.TimerManager.RemainingTime(_revertTimer.Value);
    }

    private void BroadCastState(string? ejectedPlayerId)
    {
        Room.BroadCast(PacketSerializer.Serialize(CreateStatePacket(ejectedPlayerId), true));
    }
}
