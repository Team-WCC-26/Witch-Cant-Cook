using System.Collections.Concurrent;
using System.Security.Cryptography;
using MemoryPack;
using Protocol;
using SuperSocket.Connection;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Session;

namespace Server;

/// <summary>
/// 세션 레코드 상태 (레코드 단위 lock으로 전이를 검증해 이중 전이를 방지한다)
/// </summary>
public enum SessionRecordState
{
    Active,      // 세션이 활성 바인딩 중
    Detached,    // 연결 끊김, 유예 시간 동안 Player/Room 상태 보존
    Rebinding,   // 재접속 요청 수락, 재바인딩 작업 실행 대기/진행 중
    Syncing,     // 재바인딩 완료, 스냅샷 전송 중
    AwaitingAck, // 스냅샷 전송 완료, ACK 수신 대기 중
    Expired,     // 만료 (토큰 무효)
}

/// <summary>
/// 재접속 대비 세션/플레이어 바인딩 레코드
/// </summary>
public class SessionRecord
{
    // 상태 전이 검증용 레코드 락 (lock 안에서 Room 상태는 절대 변경하지 않는다 - Room 작업은 항상 room.PushJob)
    public readonly object Lock = new();

    public Player Player { get; init; } = null!;
    public Room? Room { get; set; }
    public string? TokenHash { get; init; }     // 토큰 SHA-256 해시 (원문은 저장/로그하지 않는다)
    public long TokenExpiresAtMs { get; set; }  // UTC Unix ms (Detached 전환마다 유예 시간으로 갱신)
    public Session? Session { get; set; }       // 현재 바인딩 세션
    public Session? NewSession { get; set; }    // Rebinding 진행 중인 신세션
    public int BindingGeneration { get; set; }
    public SessionRecordState State { get; set; }
    public long DetachedAtMs { get; set; }      // 유예 시작 시각 (모노토닉)
    public long SyncStartedAtMs { get; set; }   // 스냅샷 전송/ACK 대기 시작 시각 (모노토닉)
    public long SnapshotId { get; set; }
    public byte[]? SnapshotPayload { get; set; } // 스냅샷 원본 payload (Syncing 중 청크 유실 복구 재전송용)
    public long LastReconnectRequestId { get; set; }
}

/// <summary>
/// 세션 레지스트리: Ping-Heartbeat 감시, 재접속 유예/펜싱, 스냅샷 ACK 감시를 담당한다
/// <para/> 상태 전이는 레코드 lock으로 검증하고 record/player 상태를 같은 임계구역에서 함께 갱신하며,
/// Room 상태 변경은 항상 room.PushJob(샤드 작업)으로 수행한다
/// </summary>
public class SessionRegistry
{
    // === 설정값 (환경변수 오버라이드 허용, 기본값은 설계값) ===
    public static readonly long HeartbeatTimeoutMs = ReadMs("WCC_HEARTBEAT_TIMEOUT_MS", 45_000);
    public static readonly long GracePeriodMs = ReadMs("WCC_GRACE_PERIOD_MS", 120_000);
    public static readonly long SnapshotAckTimeoutMs = ReadMs("WCC_SNAPSHOT_ACK_TIMEOUT_MS", 30_000);
    public static readonly long HeartbeatProbeIntervalMs = ReadMs("WCC_HEARTBEAT_PROBE_INTERVAL_MS", 5_000);

    // === 기능 스위치 (기본 비활성화) ===
    // 하트비트 연결 감지 스위치: 스윕의 세션 무응답 타임아웃 검사와 S_Heartbeat 프로브 전송을 담당한다.
    // false이면 무응답 연결을 종료하지 않는다(기존 동작). 재활성화 방법: 이 값을 true로 변경한다.
    public static bool EnableHeartbeat = false;

    // 토큰 기반 재접속 스위치: C_SessionHello의 토큰 발급/세션 레코드 등록을 담당한다.
    // false이면 Hello를 무시해 토큰이 발급되지 않고 레코드가 생성되지 않으며,
    // 모든 세션이 기존 legacy 경로(연결 끊김 시 즉시 룸 제거)로 동작한다.
    // 재활성화 방법: 이 값을 true로 변경한다 (이후 연결되는 세션부터 토큰이 발급된다).
    public static bool EnableReconnect = false;

    // 스냅샷 청크 크기 (클라 수신 버퍼 64KiB 제한 대비)
    public const int SnapshotChunkSize = 48 * 1024;

    // 하트비트 타임아웃 감시 대상 세션 (레코드 유무와 무관하게 모든 연결 세션)
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    private readonly ConcurrentDictionary<string, SessionRecord> _recordsByPlayerId = new();
    private readonly ConcurrentDictionary<string, SessionRecord> _recordsByTokenHash = new();

    private Timer? _sweepTimer;

    private long _nextSnapshotId = 1;

    // 서버 시작 시 스윕 타이머 구동 (1초 주기, static 메서드 그룹 콜백)
    public void Start()
    {
        if (_sweepTimer != null) return;

        _sweepTimer = new Timer(Sweep, this, 1000, 1000);
    }

    public void Stop()
    {
        _sweepTimer?.Dispose();
        _sweepTimer = null;
    }

    // === 세션 연결: 하트비트 타임아웃 감시 대상에 등록한다 (레코드 유무와 무관) ===
    public void OnConnected(Session session)
    {
        // 연결 직후 무응답 오판정을 막기 위해 수신 시각을 초기화한다
        session.LastReceivedAtMs = Environment.TickCount64;

        _sessions[session.SessionID] = session;
    }

    // === Hello: 재접속 토큰 발급 및 레코드 등록 ===
    public void OnHello(Session session, SessionHelloPacket packet)
    {
        // 재접속 기능 비활성화: 토큰을 발급하지 않고(레코드 미생성) 요청을 무시한다
        // (세션은 첫 일반 패킷에서 Live로 전환되어 기존 legacy 경로로 동작한다)
        if (!EnableReconnect) return;

        // 아래 경우의 Hello는 무시한다 (멱등/펜싱):
        // - 이미 협상된 세션의 중복 Hello
        // - 재바인딩/스냅샷 동기화 중인 세션(Rebinding 페이즈)의 Hello:
        //   재접속 요청 후 Hello 재전송이 임시 레코드로 BoundRecord/세대를 덮어써
        //   스냅샷 ACK 무시와 레코드 누수를 유발한다
        // - 폐기되었거나 이미 닫힌 세션의 Hello (레코드 등록 금지)
        if (session.Negotiated) return;
        if (session.Phase != SessionPhase.Handshaking) return;
        if (session.BoundRecord != null) return;
        if (session.Superseded) return;
        if (session.State == SessionState.Closed) return;

        session.Negotiated = true;

        // Reconnect 미지원 클라이언트: 토큰 없이 기존 동작으로 진행한다
        if (((SessionCapability)packet.Capabilities & SessionCapability.Reconnect) == 0)
        {
            session.Phase = SessionPhase.Live;
            return;
        }

        // 256bit 재접속 토큰 발급: 원문은 응답으로 1회 전달하고 서버는 해시만 보관한다
        // (해시는 재요청 시 클라가 보내는 토큰 문자열과 동일한 기준으로 계산한다)
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Base64UrlEncode(tokenBytes);
        string tokenHash = Sha256Hex(System.Text.Encoding.UTF8.GetBytes(token));

        // 안정 플레이어 식별자 발급: 이후 모든 동기화 패킷이 이 ID를 사용한다
        string stablePlayerId = Guid.NewGuid().ToString("N");

        long tokenExpiresAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + GracePeriodMs;

        session.Player.PlayerId = stablePlayerId;
        session.Phase = SessionPhase.Live;

        Register(session, session.Player, tokenHash, tokenExpiresAtMs);

        SessionTicketPacket ticket = new()
        {
            ReconnectToken = token,
            StablePlayerId = stablePlayerId,
            TokenExpiresAtMs = tokenExpiresAtMs,
            ProtocolVersion = packet.ProtocolVersion,
            Capabilities = (int)(SessionCapability.Reconnect | SessionCapability.ServerHeartbeat)
        };

        // S_SessionTicket은 제어 패킷이므로 연결 상태와 무관하게 즉시 전송한다
        session.Player.SendControl(PacketSerializer.Serialize(ticket, true));

        Console.WriteLine($"[SessionRegistry] hello accepted: session={session.SessionID}, player={stablePlayerId}");
    }

    // === 연결 종료: 즉시 정리하지 않고 유예 시간 동안 Player/Room 상태를 보존한다 ===
    public void OnClosed(Session session)
    {
        // 하트비트 타임아웃 감시 대상에서 제거한다
        _sessions.TryRemove(session.SessionID, out _);

        SessionRecord? record = session.BoundRecord;

        if (record == null)
        {
            // 레코드 없는(토큰 없는) 세션: 기존 동작대로 즉시 룸에서 제거한다 (유예 보존 없음)
            // 룸 멤버십 확정은 샤드 작업의 실행 시점 재조회(RemovePlayerFinal 멱등 가드)에 맡기고,
            // 참가 작업 도중 종료된 경우는 참가 작업의 확정 직후 재검증 롤백이 정리한다
            Player player = session.Player;

            player.ConnectionState = PlayerConnectionState.Detached;

            Room? legacyRoom = player.Room;

            if (legacyRoom != null)
            {
                PushFinalCleanup(legacyRoom, player, session.BindingGeneration);
            }

            return;
        }

        Player recordPlayer = record.Player;
        Room? room = null;
        int generation = 0;

        lock (record.Lock)
        {
            // 구세션 종료 이벤트: 이미 재바인딩됐거나(세션 불일치) 세대가 어긋난 오래된 이벤트면 무시한다 (세대 펜싱)
            if (!ReferenceEquals(record.Session, session)) return;
            if (record.BindingGeneration != session.BindingGeneration) return;

            record.Session = null;
            record.NewSession = null;
            recordPlayer.ConnectionState = PlayerConnectionState.Detached;

            // 룸 캡처는 Detached 쓰기 이후에 수행한다 (참가 작업 실행 중 경합 보정 최대화)
            room = recordPlayer.Room;

            record.Room = room;

            // 룸 멤버십 판단(로비 전용 즉시 만료)을 close 스레드에서 하지 않는다:
            // 참가 작업 실행 전/중 경합에서 잘못 만료되면 정리 대상이 상실되어
            // 플레이어가 룸에 영구 잔류한다. 무조건 유예를 시작하고 룸 확정/회수는
            // 샤드 작업(아래 보정 job)과 스윕 만료 정리가 담당한다.
            BeginGrace(record, Environment.TickCount64);

            generation = session.BindingGeneration;
        }

        if (room != null)
        {
            // 룸 확정 보정: 참가 작업이 막 실행됐을 수 있으므로 샤드 작업에서 Player.Room을 재조회해 기록한다
            room.PushJob(() =>
            {
                lock (record.Lock)
                {
                    if (record.State != SessionRecordState.Detached) return;   // 재접속/만료됨
                    if (record.BindingGeneration != generation) return;        // 재접속으로 세대 변경

                    record.Room = recordPlayer.Room ?? room;
                }
            });
        }

        Console.WriteLine($"[SessionRegistry] session detached: session={session.SessionID}, player={recordPlayer.PlayerId}");
    }

    // === 재접속 요청: 토큰 해시 검증 후 Detached → Rebinding 전이 (lock으로 1회 보장) ===
    public void OnReconnectRequest(Session session, ReconnectRequestPacket packet)
    {
        string tokenHash = Sha256Hex(System.Text.Encoding.UTF8.GetBytes(packet.ReconnectToken));

        if (!_recordsByTokenHash.TryGetValue(tokenHash, out SessionRecord? record))
        {
            SendReconnectReject(session, ReconnectFailureReason.InvalidToken);
            return;
        }

        bool reject = false;
        ReconnectFailureReason reason = ReconnectFailureReason.None;
        bool expireAndCleanup = false;
        bool resendResult = false;
        bool resendSnapshot = false;
        int resultGeneration = 0;
        long resultSnapshotId = 0;
        string resultRoomId = "";
        byte[]? resultPayload = null;

        lock (record.Lock)
        {
            switch (record.State)
            {
                case SessionRecordState.Expired:
                    // 만료된 레코드: 거부하고 누락됐을 수 있는 최종 정리를 멱등 수행한다
                    reject = true;
                    reason = ReconnectFailureReason.Expired;
                    expireAndCleanup = true;

                    break;

                case SessionRecordState.Active:
                    if (ReferenceEquals(record.Session, session) && record.SnapshotId != 0
                        && packet.RequestId == record.LastReconnectRequestId)
                    {
                        // 현재 바인딩 세션의 동일 요청 재전송: 결과만 재전송한다 (멱등)
                        resendResult = true;
                        resultGeneration = record.BindingGeneration;
                        resultSnapshotId = record.SnapshotId;
                        resultRoomId = record.Room?.Id ?? "";
                    }
                    else
                    {
                        // 다른 세션이 활성 바인딩 중
                        reject = true;
                        reason = ReconnectFailureReason.SessionActive;
                    }

                    break;

                case SessionRecordState.Rebinding:
                case SessionRecordState.Syncing:
                case SessionRecordState.AwaitingAck:
                    if (!ReferenceEquals(record.NewSession, session))
                    {
                        // 다른 신세션이 재바인딩을 진행 중
                        reject = true;
                        reason = ReconnectFailureReason.SessionActive;

                        break;
                    }

                    record.LastReconnectRequestId = packet.RequestId;

                    if (record.State == SessionRecordState.Rebinding)
                    {
                        // 재바인딩 작업 실행 전: 결과와 스냅샷이 곧 전송되므로 응답 없이 대기한다
                        break;
                    }

                    // Syncing/AwaitingAck: 기존 결과와 스냅샷 청크를 재전송한다 (청크 유실 복구)
                    resendSnapshot = true;
                    resultGeneration = record.BindingGeneration;
                    resultSnapshotId = record.SnapshotId;
                    resultRoomId = record.Room?.Id ?? "";
                    resultPayload = record.SnapshotPayload;

                    break;

                case SessionRecordState.Detached:
                    if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > record.TokenExpiresAtMs)
                    {
                        // 토큰 유효기간 경과: 만료 처리 + 최종 정리 후 거부한다
                        reject = true;
                        reason = ReconnectFailureReason.Expired;
                        expireAndCleanup = true;

                        break;
                    }

                    // Detached → Rebinding 전이 (1회 보장)
                    record.State = SessionRecordState.Rebinding;
                    record.LastReconnectRequestId = packet.RequestId;
                    record.NewSession = session;
                    record.Session = session;
                    record.Room = record.Player.Room;

                    // 신세션의 Hello로 생성했던 미사용 레코드가 있으면 무효화한다 (이 세션은 구 레코드로 재바인딩됨)
                    SessionRecord? ownRecord = session.BoundRecord;

                    if (ownRecord != null && !ReferenceEquals(ownRecord, record))
                    {
                        ExpireRecord(ownRecord);
                    }

                    // 재바인딩 작업 전 세대를 레코드 세대와 맞춘다 (Rebinding 중 종료 이벤트의 세대 펜싱 성립)
                    session.BindingGeneration = record.BindingGeneration;
                    session.BoundRecord = record;

                    // 재바인딩 완료 전 임시 Player의 유령 참가를 막는다 (유지보수 패킷 외 전부 폐기)
                    session.Phase = SessionPhase.Rebinding;

                    break;
            }
        }

        if (reject)
        {
            if (expireAndCleanup)
            {
                ExpireAndCleanupRecord(record);
            }

            SendReconnectReject(session, reason);
            return;
        }

        if (resendResult)
        {
            SendReconnectAccepted(session, record, resultGeneration, resultSnapshotId, resultRoomId);
            return;
        }

        if (resendSnapshot)
        {
            SendReconnectAccepted(session, record, resultGeneration, resultSnapshotId, resultRoomId);

            if (resultPayload != null)
            {
                SendSnapshotChunks(session, resultSnapshotId, resultPayload);
            }

            return;
        }

        PushRebindJob(record, session);
    }

    // === 스냅샷 ACK: 검증 후 Live 전환 + pending 증분 flush ===
    public void OnSnapshotApplied(Session session, SnapshotAppliedPacket packet)
    {
        SessionRecord? record = session.BoundRecord;

        if (record == null) return;

        Player player = record.Player;
        Room? room = record.Player.Room;

        if (room == null) return;

        room.PushJob(() =>
        {
            lock (record.Lock)
            {
                // 중복/오래된 ACK: 무시 (멱등). 소유권(세션/세대)도 함께 재검증한다
                if (record.State != SessionRecordState.AwaitingAck) return;
                if (record.SnapshotId != packet.SnapshotId) return;
                if (!ReferenceEquals(record.Session, session)) return;
                if (record.BindingGeneration != session.BindingGeneration) return;

                // record 상태와 player 상태를 같은 임계구역에서 함께 갱신한다
                // (이후 끼어든 세션 종료가 Live로 되살린 player를 다시 Detached로 되돌리게 한다)
                record.State = SessionRecordState.Active;
                player.ConnectionState = PlayerConnectionState.Live;
            }

            // Live 전환 후 pending 배치의 누적 증분을 전송해 기존 증분 동기화로 복귀한다
            player.FlushPending();
        });
    }

    // === 명시적 종료(C_Disconnect): 토큰 즉시 무효화 + 최종 정리 작업 투입 + 세션 종료 ===
    public void HandleExplicitDisconnect(Session session)
    {
        // 하트비트 타임아웃 감시 대상에서 제거한다
        _sessions.TryRemove(session.SessionID, out _);

        SessionRecord? record = session.BoundRecord;

        if (record != null)
        {
            Player player = record.Player;
            Room? cleanupRoom = null;
            int cleanupGeneration = 0;
            bool owned = false;

            lock (record.Lock)
            {
                // 현재 바인딩 세션이 아닌 세션의 C_Disconnect는 완전히 무시한다 (stale 펜싱)
                if (ReferenceEquals(record.Session, session))
                {
                    player.ConnectionState = PlayerConnectionState.Detached;

                    // 룸 캡처는 Detached 쓰기 이후에 수행한다 (참가 작업의 확정 직후 재검증과
                    // record lock으로 상호 배제되어 경합 창이 닫힌다)
                    Room? room = player.Room;

                    record.Session = null;
                    record.NewSession = null;
                    record.Room = room;

                    if (room != null)
                    {
                        // 정리 job 큐잉이 확정된 뒤 레코드를 제거한다 (토큰 즉시 무효화)
                        cleanupRoom = room;
                        cleanupGeneration = record.BindingGeneration;

                        ExpireRecord(record);
                    }
                    else
                    {
                        // 룸 캡처 실패(참가 작업 실행 전/경합): 레코드를 제거하지 않고 유예 상태로 둔다
                        // (레코드 제거로 스윕 fallback을 잃어 플레이어가 룸에 영구 잔류하는 것을 막는다.
                        //  스윕 유예 만료가 실행 시점 Player.Room 재조회 정리 + 토큰 만료를 보장한다)
                        BeginGrace(record, Environment.TickCount64);
                    }

                    owned = true;
                }
            }

            if (owned && cleanupRoom != null)
            {
                // 최종 정리 확정은 샤드 작업에서 수행한다 (실행 시점 멤버십 재조회)
                PushFinalCleanup(cleanupRoom, player, cleanupGeneration);
            }
        }
        else
        {
            // 레코드 없는 세션: 토큰이 없으므로 즉시 최종 정리한다 (레코드 유무와 무관하게 정리 경로를 거친다)
            // 룸 멤버십 확정은 샤드 작업의 실행 시점 재조회에 맡긴다
            Player player = session.Player;

            player.ConnectionState = PlayerConnectionState.Detached;

            Room? legacyRoom = player.Room;

            if (legacyRoom != null)
            {
                PushFinalCleanup(legacyRoom, player, session.BindingGeneration);
            }
        }

        _ = CloseQuietlyAsync(session);
    }

    // === 룸 소멸 시 해당 룸에 묶인 Detached 레코드의 토큰을 무효화한다 (RoomManager 위임) ===
    public void InvalidateRoomRecords(string roomId)
    {
        foreach (var record in _recordsByPlayerId.Values)
        {
            lock (record.Lock)
            {
                if (record.State != SessionRecordState.Detached) continue;
                if (record.Room?.Id != roomId) continue;

                ExpireRecord(record);
            }
        }
    }

    #region 내부 처리

    // Hello로 협상된 세션 등록
    private void Register(Session session, Player player, string tokenHash, long tokenExpiresAtMs)
    {
        SessionRecord record = new()
        {
            Player = player,
            Room = null,
            TokenHash = tokenHash,
            TokenExpiresAtMs = tokenExpiresAtMs,
            Session = session,
            BindingGeneration = 1,
            State = SessionRecordState.Active,
        };

        session.BoundRecord = record;
        session.BindingGeneration = 1;

        _recordsByPlayerId[player.PlayerId] = record;
        _recordsByTokenHash[tokenHash] = record;
    }

    // 레코드 만료 처리 (반드시 record.Lock 보유 중 호출: 딕셔너리 제거만 수행하고 Room 상태는 건드리지 않는다)
    private void ExpireRecord(SessionRecord record)
    {
        record.State = SessionRecordState.Expired;

        if (record.TokenHash != null)
        {
            _recordsByTokenHash.TryRemove(record.TokenHash, out _);
        }

        _recordsByPlayerId.TryRemove(record.Player.PlayerId, out _);
    }

    // 만료 + 룸 최종 정리 통합 루틴 (lock 미보유 컨텍스트용, 멱등):
    // 토큰을 무효화하고 보존 중인 룸 상태의 최종 정리 작업을 투입한다 (모든 만료 경로가 이 루틴을 사용한다)
    private void ExpireAndCleanupRecord(SessionRecord record)
    {
        lock (record.Lock)
        {
            if (record.State != SessionRecordState.Expired)
            {
                ExpireRecord(record);
            }
        }

        Room? room = record.Room ?? record.Player.Room;

        if (room != null)
        {
            PushFinalCleanup(room, record.Player, record.BindingGeneration);
        }
    }

    // 만료/명시적 종료된 플레이어의 룸 최종 정리 작업 투입 (멱등)
    // 만료(토큰 무효) 이후에는 재접속이 불가하므로, 만료 세대 이후의 재바인딩이 없으면 반드시 실행한다
    private static void PushFinalCleanup(Room room, Player player, int generation)
    {
        room.PushJob(() =>
        {
            // 만료 세대 이후 재바인딩이 있으면(유예 중 재접속 성공) 정리하지 않는다
            if (player.Session is Session boundSession && boundSession.BindingGeneration > generation) return;

            room.RemovePlayerFinal(player);
        });
    }

    // Detached 전환: 유예를 시작하고 토큰 수명을 유예 시간으로 갱신한다
    // (Active/Syncing 중에는 만료를 확장하지 않아 사실상 무제한이다 - 반드시 record.Lock 보유 중 호출)
    private static void BeginGrace(SessionRecord record, long nowMs)
    {
        record.State = SessionRecordState.Detached;
        record.DetachedAtMs = nowMs;
        record.TokenExpiresAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + GracePeriodMs;
    }

    // 재바인딩 작업: 반드시 Room 소유 샤드 작업 안에서 일관 시점에 처리한다
    private void PushRebindJob(SessionRecord record, Session newSession)
    {
        Player player = record.Player;
        Room? room = record.Room;

        if (room == null)
        {
            // 보존된 방 상태가 없으면 재접속을 수락할 수 없다: 만료 + 최종 정리 후 거부한다
            ExpireAndCleanupRecord(record);

            // 세션을 일반 세션으로 복귀시킨 뒤 거부한다 (Rebinding 페이즈 고립 방지)
            newSession.Phase = SessionPhase.Live;

            SendReconnectReject(newSession, ReconnectFailureReason.Expired);
            return;
        }

        room.PushJob(() =>
        {
            long snapshotId = 0;
            int generation = 0;
            bool expire = false;

            // 레코드 상태 전이와 세션 교체를 한 임계구역에서 수행한다 (소유권 재검증)
            lock (record.Lock)
            {
                if (record.State != SessionRecordState.Rebinding) return;            // 유예 만료/명시적 종료 등으로 해제됨
                if (!ReferenceEquals(record.NewSession, newSession)) return;        // 다른 신세션이 선점함

                if (player.Room == null || !ReferenceEquals(player.Room, room))
                {
                    // 방이 사라짐: 재접속을 수락할 수 없다 (만료 + 정리 작업 투입은 lock 밖에서 수행)
                    ExpireRecord(record);
                    expire = true;
                }
                else
                {
                    // 구세션 펜싱: 재바인딩으로 대체되는 세션은 이후 모든 패킷을 폐기한다
                    if (player.Session is Session oldSession && !ReferenceEquals(oldSession, newSession))
                    {
                        oldSession.Superseded = true;
                    }

                    record.State = SessionRecordState.Syncing;
                    record.SyncStartedAtMs = Environment.TickCount64;
                    record.BindingGeneration++;
                    generation = record.BindingGeneration;
                    record.SnapshotId = Interlocked.Increment(ref _nextSnapshotId);
                    snapshotId = record.SnapshotId;
                    record.Room = room;
                    record.SnapshotPayload = null;

                    // 세션만 교체한다: Room.Enter 재호출, Entity 재등록, Player 중복 추가 없음
                    // (record 상태와 player/세션 상태를 같은 임계구역에서 함께 갱신한다)
                    player.Session = newSession;
                    newSession.Player = player;
                    newSession.BindingGeneration = generation;
                    newSession.Superseded = false;
                    newSession.Phase = SessionPhase.Live;
                    player.ConnectionState = PlayerConnectionState.Syncing;
                    player.ResetBatches();
                }
            }

            if (expire)
            {
                ExpireAndCleanupRecord(record);

                // 세션을 일반 세션으로 복귀시킨 뒤 거부한다 (Rebinding 페이즈 고립 방지)
                newSession.Phase = SessionPhase.Live;

                SendReconnectReject(newSession, ReconnectFailureReason.Expired);
                return;
            }

            // 서버 현재 상태 전체 스냅샷을 같은 잡 안에서 일관 시점에 생성한다 (Dirty Consume 없이 읽기만)
            SnapshotData snapshot = room.CreateReconnectSnapshot();

            // ACK 전 재요청(청크 유실 복구)에 대비해 payload를 보관한다
            byte[] payload = MemoryPackSerializer.Serialize(snapshot);

            lock (record.Lock)
            {
                record.SnapshotPayload = payload;
            }

            // 전송은 세션이 아직 열려 있을 때만 한다
            if (newSession.State == SessionState.Closed) return;

            SendReconnectAccepted(newSession, record, generation, snapshotId, room.Id);

            SendSnapshotChunks(newSession, snapshotId, payload);

            // 스냅샷 전송 완료: ACK 대기 상태로 전환한다 (ACK 전 증분은 pending 배치에 보관)
            // 소유권을 재검증한 뒤 record/player 상태를 같은 임계구역에서 함께 갱신한다
            lock (record.Lock)
            {
                if (record.State != SessionRecordState.Syncing) return;             // 소유권 상실 (끊김/ACK 타임아웃)
                if (!ReferenceEquals(record.Session, newSession)) return;
                if (record.BindingGeneration != generation) return;

                record.State = SessionRecordState.AwaitingAck;
                player.ConnectionState = PlayerConnectionState.AwaitingAck;
            }
        });
    }

    // 스냅샷 청크 분할 전송 (제어 경로: 연결 상태와 무관하게 즉시 전송)
    private static void SendSnapshotChunks(Session session, long snapshotId, byte[] payload)
    {
        int chunkCount = (payload.Length + SnapshotChunkSize - 1) / SnapshotChunkSize;

        if (chunkCount == 0) chunkCount = 1;

        for (int i = 0; i < chunkCount; i++)
        {
            int offset = i * SnapshotChunkSize;
            int size = Math.Min(SnapshotChunkSize, payload.Length - offset);

            ReconnectSnapshotPacket chunk = new()
            {
                SnapshotId = snapshotId,
                ChunkIndex = i,
                ChunkCount = chunkCount,
                IsFinal = i == chunkCount - 1,
                Payload = payload[offset..(offset + size)]
            };

            session.Player.SendControl(PacketSerializer.Serialize(chunk, true));
        }
    }

    // 1초 주기 스윕: 하트비트 타임아웃 / 유예 만료 정리 / 스냅샷 ACK 타임아웃 / 하트비트 프로브 전송
    private void Sweep(object? state)
    {
        long now = Environment.TickCount64;

        List<Session>? sessionsToClose = null;
        List<(Room Room, Player Player, int Generation)>? finalCleanups = null;
        List<Session>? probes = null;

        // 하트비트 타임아웃: 레코드 유무와 무관하게 모든 연결 세션을 감시한다
        // (레코드 없는 기존 클라이언트의 무응답 연결도 종료 대상에 포함된다)
        // 비활성화 스위치가 꺼져 있으면 무응답 연결을 종료하지 않는다 (기존 동작)
        if (EnableHeartbeat)
        {
            foreach (var session in _sessions.Values)
            {
                if (session.State == SessionState.Closed) continue;

                if (now - session.LastReceivedAtMs > EffectiveHeartbeatTimeoutMs(session))
                {
                    // 비정상 연결 감지: 세션 종료만 요청한다 (상태 전이는 종료 이벤트에서 수행)
                    (sessionsToClose ??= new()).Add(session);
                }
            }
        }

        foreach (var record in _recordsByPlayerId.Values)
        {
            lock (record.Lock)
            {
                switch (record.State)
                {
                    case SessionRecordState.Active:
                    {
                        Session? session = record.Session;

                        if (session == null || session.State == SessionState.Closed)
                        {
                            // 소유 세션이 없거나 닫혔는데 종료 이벤트가 레코드 전이를 못 했다
                            // (Hello/종료 경합 등): Detached로 전환해 유예/정리 경로로 보낸다
                            // (닫힌 세션에 Active 레코드가 영구 잔류하는 것을 방지)
                            record.Session = null;
                            record.NewSession = null;
                            record.Room = record.Room ?? record.Player.Room;
                            record.Player.ConnectionState = PlayerConnectionState.Detached;

                            BeginGrace(record, now);

                            break;
                        }

                        if (EnableHeartbeat && now - session.LastProbeSentMs >= HeartbeatProbeIntervalMs)
                        {
                            // 협상 세션에 주기적 하트비트 프로브를 전송한다
                            session.LastProbeSentMs = now;
                            (probes ??= new()).Add(session);
                        }

                        break;
                    }

                    case SessionRecordState.Detached:
                    {
                        if (now - record.DetachedAtMs < GracePeriodMs) break;

                        // 유예 만료: 레코드 만료가 기준이므로 player 상태 불일치와 무관하게
                        // 최종 정리 작업을 투입한다 (멱등 - 재접속 거부 상태로 전환)
                        ExpireRecord(record);

                        Room? room = record.Room ?? record.Player.Room;

                        if (room != null)
                        {
                            (finalCleanups ??= new()).Add((room, record.Player, record.BindingGeneration));
                        }

                        break;
                    }

                    case SessionRecordState.Syncing:
                    case SessionRecordState.AwaitingAck:
                    {
                        // ACK 대기 타임아웃: 지연 시뮬레이션 세션은 왕복 지연만큼 관대하게 확장한다
                        // (하트비트 타임아웃과 동일 기준)
                        long ackTimeoutMs = SnapshotAckTimeoutMs;

                        if (record.Session != null
                            && LatencySimulator.TryGetLatency(record.Session, out int receiveDelayMs, out int sendDelayMs))
                        {
                            long extended = SnapshotAckTimeoutMs + receiveDelayMs + sendDelayMs;

                            if (extended > ackTimeoutMs) ackTimeoutMs = extended;
                        }

                        if (now - record.SyncStartedAtMs < ackTimeoutMs) break;

                        // ACK 타임아웃: 신세션만 종료하고 Detached로 복귀한다 (유예 시간 재시작)
                        // record/player 상태를 같은 임계구역에서 함께 갱신한다 (끼어든 종료 이벤트에 덮어쓰이지 않게)
                        Session? stale = record.Session;

                        record.State = SessionRecordState.Detached;
                        record.Session = null;
                        record.NewSession = null;
                        record.Player.ConnectionState = PlayerConnectionState.Detached;
                        BeginGrace(record, now);

                        if (stale != null)
                        {
                            (sessionsToClose ??= new()).Add(stale);
                        }

                        break;
                    }
                }
            }
        }

        // lock 밖에서 종료/정리/전송을 수행한다
        if (sessionsToClose != null)
        {
            foreach (var session in sessionsToClose)
            {
                _ = CloseQuietlyAsync(session);
            }
        }

        if (finalCleanups != null)
        {
            foreach (var (room, player, generation) in finalCleanups)
            {
                PushFinalCleanup(room, player, generation);
            }
        }

        if (probes != null)
        {
            foreach (var session in probes)
            {
                HeartbeatPacket probe = new()
                {
                    TimestampMs = TimeUtil.NowMs()
                };

                // S_Heartbeat은 제어 패킷이므로 연결 상태와 무관하게 즉시 전송한다
                LatencySimulator.SendAsync(session, PacketSerializer.Serialize(probe, true));
            }
        }
    }

    // 무응답 판정 타임아웃: 지연 시뮬레이션이 활성인 세션은 왕복 지연만큼 관대하게 확장한다
    private static long EffectiveHeartbeatTimeoutMs(Session session)
    {
        long timeoutMs = HeartbeatTimeoutMs;

        if (LatencySimulator.TryGetLatency(session, out int receiveDelayMs, out int sendDelayMs))
        {
            long extended = 3L * HeartbeatProbeIntervalMs + receiveDelayMs + sendDelayMs;

            if (extended > timeoutMs) timeoutMs = extended;
        }

        return timeoutMs;
    }

    private static void SendReconnectReject(Session session, ReconnectFailureReason reason)
    {
        ReconnectResultPacket packet = new()
        {
            Accepted = false,
            FailureReason = reason,
        };

        // S_ReconnectResult는 제어 패킷이므로 연결 상태와 무관하게 즉시 전송한다
        session.Player.SendControl(PacketSerializer.Serialize(packet, true));

        Console.WriteLine($"[SessionRegistry] reconnect rejected: session={session.SessionID}, reason={reason}");
    }

    private static void SendReconnectAccepted(Session session, SessionRecord record, int generation, long snapshotId, string roomId)
    {
        ReconnectResultPacket packet = new()
        {
            Accepted = true,
            FailureReason = ReconnectFailureReason.None,
            StablePlayerId = record.Player.PlayerId,
            RoomId = roomId,
            BindingGeneration = generation,
            SnapshotId = snapshotId
        };

        // S_ReconnectResult는 제어 패킷이므로 연결 상태와 무관하게 즉시 전송한다
        session.Player.SendControl(PacketSerializer.Serialize(packet, true));
    }

    // 세션 종료 요청 (이미 닫힌 세션 등의 실패는 무시)
    private static async ValueTask CloseQuietlyAsync(IAppSession session)
    {
        try
        {
            if (session.State != SessionState.Closed)
            {
                await session.CloseAsync(CloseReason.LocalClosing);
            }
        }
        catch (Exception)
        {
            // 종료 요청 실패는 무시
        }
    }

    // 토큰 해시 (SHA-256 hex). 원문은 저장/로그하지 않는다.
    private static string Sha256Hex(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data));
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // 환경변수 오버라이드 가능 설정값 (기본값은 설계값 유지)
    private static long ReadMs(string envName, long defaultValueMs)
    {
        if (!long.TryParse(Environment.GetEnvironmentVariable(envName), out long value) || value <= 0) return defaultValueMs;

        return value;
    }

    #endregion
}
