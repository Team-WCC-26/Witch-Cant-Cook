using Protocol;
using SuperSocket;
using SuperSocket.Connection;
using SuperSocket.Server;

namespace Server;

/// <summary>
/// 세션 로컬 협상 단계 (SessionRecord 상태와 별개인 세션 자체의 상태)
/// </summary>
public enum SessionPhase
{
    Handshaking, // SessionHello 수신 전
    Rebinding,   // 재접속 요청 수락, 재바인딩 완료 전 (유지보수 패킷만 허용)
    Live,        // Hello 협상 완료 또는 Hello 미사용(기존 동작) 세션
}

public class Session : AppSession
{
    public Player Player { get; set; } = new();

    // 마지막 패킷 수신 시각 (모노토닉, Environment.TickCount64)
    public long LastReceivedAtMs { get; set; }

    // 마지막 하트비트 프로브 전송 시각 (모노토닉)
    public long LastProbeSentMs { get; set; }

    // 재접속으로 대체되어 폐기된 세션 (모든 패킷 폐기)
    public volatile bool Superseded;

    // 세션 바인딩 세대 (재접속마다 증가, 게임플레이 작업 펜싱에 사용)
    public volatile int BindingGeneration;

    // SessionHello로 재접속 협상이 완료된 세션
    public bool Negotiated { get; set; }

    // 협상 단계
    public volatile SessionPhase Phase = SessionPhase.Handshaking;

    // 이 세션이 바인딩된 세션 레코드 (Hello로 생성되거나 재접속으로 교체된다)
    public SessionRecord? BoundRecord { get; set; }

    protected override ValueTask OnSessionConnectedAsync()
    {
        Player.PlayerId = SessionID;
        Player.Session = this;

        // 하트비트 타임아웃 감시 대상에 등록한다 (레코드 유무와 무관)
        ServerContext.Instance.SessionRegistry.OnConnected(this);

        return base.OnSessionConnectedAsync();
    }

    protected override async ValueTask OnSessionClosedAsync(CloseEventArgs e)
    {
        // 지연 시뮬 큐를 먼저 정리해 종료 세션에 오래된 패킷이 유출되지 않게 한다
        await LatencySimulator.RemoveAsync(SessionID, this);

        // 즉시 Player/Room을 정리하지 않고 레지스트리에 위임한다
        // (유예 시간 동안 Player/Room 상태를 보존하며, 만료 후 최종 정리 작업으로 제거된다)
        ServerContext.Instance.SessionRegistry.OnClosed(this);

        await base.OnSessionClosedAsync(e);
    }

    // Dispatch 허용 여부 게이팅 (협상 단계/폐기 여부/플레이어 연결 상태 기반)
    public bool AllowDispatch(PacketId id)
    {
        if (Superseded) return false;

        switch (Phase)
        {
            case SessionPhase.Handshaking:
                // Hello 협상 전에는 세션 협상/하트비트 패킷만 허용한다
                if (IsSessionMaintenancePacket(id)) return true;

                // Hello를 보내지 않는 기존 클라이언트: 첫 일반 패킷에서 라이브로 전환해 기존 동작을 유지한다
                Phase = SessionPhase.Live;

                return true;

            case SessionPhase.Rebinding:
                // 재접속 수락 후 재바인딩 완료 전: 임시 Player의 유령 참가를 막기 위해
                // 유지보수(hello/ping/heartbeat/재접속/스냅샷 ACK/명시적 종료) 외 전부 폐기한다
                return IsSessionMaintenancePacket(id);

            case SessionPhase.Live:
                return IsAllowedForPlayerState(id);

            default:
                return false;
        }
    }

    // 플레이어 연결 상태별 수신 허용 패킷
    private bool IsAllowedForPlayerState(PacketId id)
    {
        // 유지보수 패킷(하트비트/세션 협상/스냅샷 ACK 포함)은 플레이어 연결 상태와 무관하게 항상 허용한다
        if (IsSessionMaintenancePacket(id)) return true;

        // 게임플레이 패킷은 바인딩 상태 검증(레코드 기준 일원화)을 통과해야 한다
        return IsGameplayBindable();
    }

    // 현재 세션이 게임플레이 패킷을 처리할 수 있는 바인딩 상태인지 검사한다
    // (협상 세션: 레코드 Active + 플레이어 Live + 현재 바인딩 세션 / 레코드 없는 세션: 기존 동작 유지)
    private bool IsGameplayBindable()
    {
        SessionRecord? record = BoundRecord;

        if (record == null) return Player.ConnectionState == PlayerConnectionState.Live;

        lock (record.Lock)
        {
            return record.State == SessionRecordState.Active
                && Player.ConnectionState == PlayerConnectionState.Live
                && ReferenceEquals(record.Session, this);
        }
    }

    // 하트비트/세션 관리 패킷: 플레이어 연결 상태와 무관하게 항상 허용한다
    // (재접속 요청/스냅샷 ACK 포함: 같은 세션의 멱등 재요청과 ACK가 연결 상태 전이와 무관하게 처리될 수 있게 한다)
    private static bool IsSessionMaintenancePacket(PacketId id)
    {
        return id is PacketId.C_SessionHello or PacketId.C_Ping or PacketId.C_Heartbeat
            or PacketId.C_ReconnectRequest or PacketId.C_SnapshotApplied or PacketId.C_Disconnect;
    }
}
