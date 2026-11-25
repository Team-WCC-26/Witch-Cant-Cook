using Cysharp.Threading.Tasks;
using Protocol;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Server
{
    /// <summary>
    /// 연결 재활용 기반 클래스. ServerManager가 하나를 소유한다.
    /// 끊김을 수신 루프 종료(FIN/예외)와 수신 정지 워치독 두 소스로 감지해 동일한 OnConnectionLost 경로(NotifyConnectionLost)로 회복 흐름에 진입한다.
    /// 연결 계층 keepalive(C_Ping)로 세션 유지 중 수신 트래픽을 보장해 워치독을 모든 상태에서 유효하게 만든다.
    /// 티켓이 있으면 기한 내 재접속 루프를, 없으면 곧바로 신규 접속 플로우로 전환한다.
    /// 스냅샷 파싱/적용/C_SnapshotApplied(ACK)는 다음 작업 범위다(현재는 청크 폐기 + 수락 후 재시도 반복).
    /// </summary>
    // 재접속 시퀀스 전체는 ServerManager.UseReconnectSequence 토글로 개폐된다 (false = 완전 레거시 동작)
    public class SessionReconnector
    {
        // === 상수 (서버 값과의 관계) ===
        private const int HelloTicketWaitMs = 3000;     // Hello 후 S_SessionTicket 대기 허용치. 미수신이면 로그 1회 후 레거시 모드로 게임플레이 정상 진행 (서버의 재접속 스위치는 옵션이므로)
        private const int IdleCheckIntervalMs = 1000;   // 끊김 감시(워치독) 검사 주기 (1초)
        private const int IdleTimeoutMs = 15000;        // 수신 정지 판정 기준. 연결 계층 keepalive가 KeepAliveIntervalMs마다 C_Ping을 보내고 서버가 제어 전송으로 즉답(S_Pong)하므로 정상 상태에선 항상 충족 - keepalive 주기의 3배 여유
        private const int KeepAliveIntervalMs = 5000;   // 연결 계층 keepalive 주기. 배포 타이틀/로비 씬에는 Ping 컴포넌트가 부착돼 있지 않고(테스트 씬에만 존재) 서버의 S_Heartbeat 프로브는 협상 세션 전용이므로, 협상 여부와 무관하게 수신 트래픽을 보장한다 (서버 C_Ping 즉답 = 제어 전송, 프로브 주기 5s와 동일 수준)
        private const int ResultWaitMs = 10000;         // S_ReconnectResult 응답 대기 (시도별 타임아웃)
        private const int AttemptIntervalMs = 5000;     // 재접속 시도 사이 간격 (서버 하트비트 프로브 주기 5s에 맞춰 시도 사이 서버 상태 변화를 관측할 수 있게)
        private const int ReconnectDeadlineMs = 90000;  // 첫 끊김 시점 기준 재접속 기한. 서버 유예 WCC_GRACE_PERIOD_MS(120s)보다 짧게 둬 기한 만료 시점에도 토큰 여유를 확보한다
        private const int AttemptWaitPollMs = 50;       // 재접속 결과 응답 폴링 간격 (핸들러가 JobWorker 큐에서 반영하므로)
        private const int EchoedHeartbeatHistoryCap = 32; // S_Heartbeat 에코 이력 상한 - 프로브 주기(5s) 기준 160초치. 초과 시 전체 이력을 비운다(아래 HandleHeartbeat 주석 근거)

        /// <summary>재접속 시도 1건의 결과</summary>
        private enum ReconnectAttemptOutcome
        {
            Accepted,  // 재접속 수락 - 스냅샷 적용 파트 미구현(다음 작업)이라 소켓을 닫고 간격마다 재시도 (중간 단계 동작)
            Rejected,  // 거절 - InvalidToken/Expired/SessionActive 등: 재접속을 포기하고 신규 접속 플로우로 진행
            Timeout,   // 응답 없음/접속 실패 - 소켓은 시도 진입부에서 정리됐다. 간격 후 재시도
        }

        // === 세션 티켓 상태 (S_SessionTicket 수신 시 저장) ===
        // ReconnectToken은 티켓 응답으로 1회만 전달되므로 이곳 저장이 클라 쪽 유일한 기회이며, 서버는 해시만 보관한다.
        // 티켓이 오면 자기 식별자가 SessionID에서 StablePlayerId로 바뀐다는 점에 주의한다.
        // 기존 동기화 로직은 그대로 두고 이 클래스는 저장과 재접속 요청에만 사용한다
        private string _reconnectToken = null;
        private string _stablePlayerId = null;
        private long _tokenExpiresAtMs = 0; // UTC Unix ms - 참고용 값 (실제 재접속 기한은 첫 끊김 + ReconnectDeadlineMs 기준)

        // 세션 일련 번호 - 재접속 등으로 이전 세션이 남긴 티켓 대기 타이머를 일련번호로 무시한다
        private int _sessionSeq = 0;

        // 끊김 처리 멱등 가드 - 수신 스레드(콜백)와 워치독(메인)이 같은 연결의 끊김을 여러 소스에서 보고할 수 있으므로
        // 원자적 경로(CAS)로만 회복 흐름에 배입시킨다. 0 = 수용, 1 = 회복 흐름 진행 중
        // (재접속 루프/신규 접속 동안 도착하는 후속 끊김 통보를 흡수, 접속별 리셋은 BeginSession의 끝줄)
        private int _recoveringFlag = 0;

        // 끊김 감시 대상 - 현재 살아 있는(검사 중인 연결 세대의) 수신 인스턴스.
        // volatile 쓰기/판독으로 콜백 세대 비교(NotifyConnectionLost)의 기준값이 된다
        private volatile PacketReceiver _watchedReceiver = null;

        // 현재 재접속 시도의 대기 중 결과 - S_ReconnectResult 핸들러가 JobWorker 큐를 타고 기록한다
        private volatile ReconnectResultPacket _pendingReconnectResult = null;

        // C_ReconnectRequest의 RequestId 증분 카운터
        private int _reconnectRequestId = 0;

        // 이미 에코를 보낸 S_Heartbeat TimestampMs 이력 - 서버는 받은 C_Heartbeat을 같은 값으로 S_Heartbeat에 에코하므로
        // 값의 단독 비교만으로 프로브/에코를 구분할 수 없고, 프로브 2개가 in-flight인 경우 단일 이전값 가드로는
        // 에코 재에코 무한 루프가 생긴다. 세션당 이력 집합으로 판정한다 (핸들러 실행은 JobWorker 큐에서 직렬화된다)
        private readonly HashSet<long> _echoedHeartbeatTimestamps = new();

        public bool HasTicket => _reconnectToken != null;

        public string ReconnectToken => _reconnectToken;

        public string StablePlayerId => _stablePlayerId;

        public long TokenExpiresAtMs => _tokenExpiresAtMs;

        /// <summary>
        /// 시스템 패킷 핸들러 등록 + 끊김 감시 워치독/연결 계층 keepalive 시작. ServerManager.Initialize 경로에서 1회 호출된다.
        /// 디스패처가 세션 교체와 무관하게 유지되고 RegisterHandler가 ID 단위 덮어쓰기이므로 재접속해도 중복 등록이 생기지 않는다
        /// </summary>
        public void Initialize()
        {
            // 재접속 시퀀스 토글(ServerManager.UseReconnectSequence)이 OFF면 완전 레거시 동작 - 시스템 핸들러 등록과 워치독/keepalive 루프 기동을 생략한다
            if (!ServerManager.Instance.UseReconnectSequence) return;

            ServerManager.Instance.RegisterHandler(PacketId.S_SessionTicket, HandleSessionTicket);
            ServerManager.Instance.RegisterHandler(PacketId.S_ReconnectResult, HandleReconnectResult);
            ServerManager.Instance.RegisterHandler(PacketId.S_Heartbeat, HandleHeartbeat);
            ServerManager.Instance.RegisterHandler(PacketId.S_ReconnectSnapshot, HandleReconnectSnapshot);

            _ = RunIdleWatchdogAsync();
            _ = RunKeepAliveLoopAsync();
        }

        /// <summary>
        /// 접속별 상태 리셋 - ServerManager.StartSessionAsync 끝(수신 태스크 기동 직후)에서 호출된다.
        /// 재접속으로 수신 인스턴스가 교체되어도 세션당 1회만 갱신된다
        /// </summary>
        public void BeginSession(PacketReceiver receiver)
        {
            // 워치독/keepalive가 보는 현재 세대의 수신 인스턴스를 교체한다
            // (volatile 쓰기 - 세대 비교 기준값 선갱신이 멱등 가드 리셋보다 선행해야 한다)
            _watchedReceiver = receiver;

            // 세션별 S_Heartbeat 에코 이력 리셋
            _echoedHeartbeatTimestamps.Clear();

            // 티켓 대기 타이머 기동 - 비차단: 이전 세션의 남은 타이머는 일련번호로 무시된다
            // (토글 OFF면 티켓 저장 흐름이 비활성이므로 타이머도 기동하지 않는다)
            if (ServerManager.Instance.UseReconnectSequence)
            {
                _ = RunTicketWaitTimerAsync(++_sessionSeq);
            }

            // 새 접속이 완전히 맺어진 지금부터는 끊김 통보를 다시 수용한다 (원자적 리셋)
            Interlocked.Exchange(ref _recoveringFlag, 0);
        }

        /// <summary>
        /// 끊김 진입점 - PacketReceiver 종료(수신 스레드)가 자기 인스턴스를 전달해 호출하고,
        /// 수신 정지 워치독(메인)은 현재 감시 중인 수신 인스턴스로 호출한다.
        /// 감시 대상과 다른 세대의 콜백은 무시(구 수신 태스크의 늦은 콜백 차단)되고, 같은 세대의 중복 발화는
        /// CAS 멱등 가드로 1회만 통과된다. 회복 흐름 본체는 PushJob(JobWorker/메인 작업)으로 마샬링한다
        /// </summary>
        public void NotifyConnectionLost(PacketReceiver source)
        {
            // 재접속 시퀀스 토글(ServerManager.UseReconnectSequence)이 OFF면 끊겨도 아무 복구 없이 무시한다 (완전 레거시)
            if (!ServerManager.Instance.UseReconnectSequence) return;

            // 세대 불일치 - 구 수신 태스크의 늦은 콜백은 자연히 차단된다
            if (!ReferenceEquals(_watchedReceiver, source)) return;

            // 멱등 가드 - 첫 CAS 승자만 회복 흐름에 배입된다 (감시/리셋도 모두 원자적 경로를 사용한다)
            if (Interlocked.CompareExchange(ref _recoveringFlag, 1, 0) != 0) return;

            // 끊김 처리의 상태 변경은 JobWorker 큐에서 순서대로 실행되도록 마샬링한다 (핸들러 작업과 같은 큐)
            ServerManager.Instance.PushJob(() => _ = RunRecoveryFlowAsync());
        }

        // === 회복 흐름 (재접속 루프 / 신규 접속) ===

        /// <summary>
        /// 회복 흐름 - 티켓이 있으면 기한 내 재접속 루프를 돌고, 없으면 곧바로 신규 접속으로 전환한다.
        /// 재접속 루프 도중의 끊김 통보는 CAS 멱등 가드가 흡수하므로 상태는 단일 진행 플래그(_recoveringFlag) 하나로 충분하다
        /// </summary>
        private async UniTask RunRecoveryFlowAsync()
        {
            try
            {
                long deadlineMs = TimeUtil.NowMs() + ReconnectDeadlineMs;

                if (_reconnectToken != null)
                {
                    // === 티켓 기반 재접속 루프 - 기한(deadlineMs) 내에서만 재시도 ===
                    while (TimeUtil.NowMs() < deadlineMs)
                    {
                        ReconnectAttemptOutcome outcome = await TryReconnectAttemptAsync();

                        if (outcome == ReconnectAttemptOutcome.Accepted)
                        {
                            Debug.Log("[Session] 재접속 수락됨 - 스냅샷 적용 파트 미구현");

                            // 스냅샷 파싱/적용/C_SnapshotApplied가 아직 없으므로(다음 작업) 수락을 받아도 현재 소켓을 닫고
                            // 간격 후 기한 내 재시도한다. 서버는 ACK가 오지 않으면 WCC_SNAPSHOT_ACK_TIMEOUT_MS(30s) 후 소켓을 닫고
                            // Detached로 복귀하며 토큰을 연장하므로, 이 수락-닫기-재시도 반복이 중간 단계 동작으로 계속된다
                            ServerManager.Instance.TeardownConnection();
                            await UniTask.Delay(AttemptIntervalMs, DelayType.UnscaledDeltaTime);
                            continue;
                        }

                        if (outcome == ReconnectAttemptOutcome.Rejected)
                        {
                            Debug.LogWarning("[Session] 재접속 거절 - 재접속을 포기하고 신규 접속 플로우로 진행");
                            break;
                        }

                        // 응답 없음/접속 실패 - 소켓 정리는 시도 진입부에서 완료됐다. 간격 후 재시도
                        await UniTask.Delay(AttemptIntervalMs, DelayType.UnscaledDeltaTime);
                    }
                }
                else
                {
                    // 티켓이 없는 경우(레거시 모드/재접속 스위치 OFF) - 재접속 루프를 생략하고 곧바로 신규 접속 플로우만 실행한다
                }

                // === 신규 접속 플로우 - 기한 만료/거절/티켓 없음 어느 경로든 이 코드로 마무리한다 ===
                await RunNewSessionFlowAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Session] 회복 흐름 예외 - {ex.GetType().Name}: {ex.Message}");

                // 예외 잔류 방지 - CAS 가드가 남아 이후의 끊김 처리를 봉인하는 일을 막는다 (원자적 리셋)
                Interlocked.Exchange(ref _recoveringFlag, 0);
            }
        }

        /// <summary>
        /// 신규 접속 플로우 - Initialize 경로(StartSessionAsync)와 동일 코드 경로로 재접속 실패 후 세션을 다시 만든다.
        /// 진입 전 세션 정리: 남아 있는 스트림(예: 재접속 거절 소켓)에 서버가 명시적 종료로 즉시 방 정리를 하도록
        /// C_Disconnect 원시 프레임(6바이트 헤더만: 길이 0 + C_Disconnect ID, 빈 본문)을 먼저 쓰고 연결을 닫는다.
        /// 죽은 스트림이면 실패는 무시된다. 재접속이 쓰고 난 토큰/티켓 상태 역시 여기서 리셋한다
        /// </summary>
        private async UniTask RunNewSessionFlowAsync()
        {
            while (true)
            {
                ServerManager.Instance.SendDisconnectFrameIfOpen();
                ServerManager.Instance.TeardownConnection();

                // 토큰/티켓 상태 리셋 - 새 접속은 hello 전송 -> 티켓 저장 -> 기존 게임플레이 흐름 순으로 진행된다
                _reconnectToken = null;
                _stablePlayerId = null;
                _tokenExpiresAtMs = 0;

                try
                {
                    await ServerManager.Instance.StartSessionAsync();
                    return;
                }
                catch (Exception ex)
                {
                    // 접속 실패 (연결 거부/호스트 도달 불가 등) - 서버가 돌아올 때까지 간격 후 반복 시도한다
                    Debug.LogWarning($"[Session] 신규 접속 실패 - {AttemptIntervalMs}ms 후 재시도 ({ex.GetType().Name}: {ex.Message})");
                    await UniTask.Delay(AttemptIntervalMs, DelayType.UnscaledDeltaTime);
                }
            }
        }

        /// <summary>
        /// 재접속 시도 1건. 진입 시 기존 TcpClient/스트림/수신 태스크를 전부 정리하고(이때 수신 전용 CTS만 취소된다 -
        /// 공용 _cts를 취소하면 JobWorker도 죽는다, 정리 표시로 구 콜백은 무음 처리된다) 새 소켓에 수신 태스크를 띄운 뒤,
        /// Hello 없이 바로 C_ReconnectRequest{ReconnectToken, RequestId(증분 카운터)}를 전송하고
        /// 시도별 타임아웃 안에 S_ReconnectResult를 기다린다
        /// </summary>
        private async UniTask<ReconnectAttemptOutcome> TryReconnectAttemptAsync()
        {
            _pendingReconnectResult = null;

            try
            {
                // 기존 연결 완전 정리 + 새 소켓/스트림/수신 태스크 수립 (Hello는 보내지 않는다)
                await ServerManager.Instance.OpenFreshConnectionAsync();

                ReconnectRequestPacket requestPacket = new()
                {
                    ReconnectToken = _reconnectToken,
                    RequestId = Interlocked.Increment(ref _reconnectRequestId),
                };
                await ServerManager.Instance.SendData(PacketSerializer.Serialize(requestPacket));

                // S_ReconnectResult 대기 - 핸들러 반영이 JobWorker 큐를 타므로 폴링으로 응답을 확인한다
                long waitedMs = 0;

                while (waitedMs < ResultWaitMs)
                {
                    await UniTask.Delay(AttemptWaitPollMs, DelayType.UnscaledDeltaTime);
                    waitedMs += AttemptWaitPollMs;

                    ReconnectResultPacket result = _pendingReconnectResult;

                    if (result == null) continue;

                    if (result.Accepted) return ReconnectAttemptOutcome.Accepted;

                    Debug.LogWarning($"[Session] 재접속 거절 - 사유 {result.FailureReason}");
                    return ReconnectAttemptOutcome.Rejected;
                }

                Debug.LogWarning($"[Session] 재접속 응답 없음 ({ResultWaitMs}ms 타임아웃) - 간격 후 재시도");
                return ReconnectAttemptOutcome.Timeout;
            }
            catch (Exception ex)
            {
                // 접속/송신 실패 - 타임아웃과 동일하게 취급해 다음 시도로 넘어간다
                Debug.LogWarning($"[Session] 재접속 시도 실패 - {ex.GetType().Name}: {ex.Message}");
                return ReconnectAttemptOutcome.Timeout;
            }
        }

        // === 감시 (워치독 / keepalive / 티켓 대기) ===

        /// <summary>
        /// 수신 정지 워치독 - IdleCheckIntervalMs 주기 검사. 세션이 살아 있는 동안(회복 흐름 밖) 이
        /// IdleTimeoutMs 이상 서버 데이터가 없으면 수신 루프 종료와 같은 NotifyConnectionLost를 발화한다.
        /// 수신 트래픽은 연결 계층 keepalive가 세션 상태와 무관하게 보장하므로(아래 RunKeepAliveLoopAsync 주석)
        /// 협상/레거시 모두에서 이 판단이 유효하다. 회복 흐름 중(재접속 시도 소켓)에는 검사를 건너뛴다
        /// </summary>
        private async UniTask RunIdleWatchdogAsync()
        {
            while (true)
            {
                await UniTask.Delay(IdleCheckIntervalMs, DelayType.UnscaledDeltaTime);

                if (Volatile.Read(ref _recoveringFlag) != 0) continue; // 회복 흐름 진행 중 - 검사 생략

                PacketReceiver receiver = _watchedReceiver;

                if (receiver == null) continue; // 세션이 아직 없는 구간 (Initialize 직후 등)

                long idleMs = TimeUtil.NowMs() - receiver.LastReceiveTickMs;

                if (idleMs > IdleTimeoutMs)
                {
                    Debug.LogWarning($"[Session] 서버 데이터 없음 {idleMs}ms - 끊김 판정 (수신 정지 워치독)");

                    NotifyConnectionLost(receiver);
                }
            }
        }

        /// <summary>
        /// 연결 계층 keepalive - 세션이 살아 있는 동안(레거시/협상 모두) KeepAliveIntervalMs마다 C_Ping을 직접 전송한다.
        /// 근거: 배포 빌드의 타이틀/로비 씬에는 Ping 컴포넌트가 부착돼 있지 않고(테스트 씬에만 존재) 서버의
        /// S_Heartbeat 프로브는 협상 세션 전용이므로, 서버 스위치/협상 여부와 무관하게 수신 트래픽(S_Pong 즉답)을
        /// 보장해 수신 정지 워치독(IdleTimeoutMs = 3배)이 모든 상태에서 유효해진다.
        /// 서버는 C_Ping에 S_Pong을 제어 전송으로 즉답하며 stale 세션의 핑은 PingPong 핸들러에서 무시한다.
        /// Ping.cs가 부착된 씬에선 핑이 중복되지만 서버 에코 처리에 무해하다 (Ping.cs는 수정 금지)
        /// </summary>
        private async UniTask RunKeepAliveLoopAsync()
        {
            while (true)
            {
                await UniTask.Delay(KeepAliveIntervalMs, DelayType.UnscaledDeltaTime);

                if (Volatile.Read(ref _recoveringFlag) != 0) continue; // 회복 흐름(재접속 시도) 중 - 세션이 없는 구간

                PacketReceiver receiver = _watchedReceiver;

                if (receiver == null) continue; // 살아 있는 세션이 없는 구간 (Initialize 직후/신규 접속 완료 전)

                PingPongPacket pingPacket = new()
                {
                    TimeMs = TimeUtil.NowMs(),
                };

                _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(pingPacket));
            }
        }

        /// <summary>
        /// 비차단 티켓 대기 - 접속(Hello 전송) 후 HelloTicketWaitMs 안에 S_SessionTicket이 오지 않으면 로그 1회만 남긴다.
        /// 게임플레이는 어떤 경우에도 정상 진행되며, 이전 세션(일련번호 불일치)의 타이머는 무시된다
        /// </summary>
        private async UniTask RunTicketWaitTimerAsync(int sessionSeq)
        {
            await UniTask.Delay(HelloTicketWaitMs, DelayType.UnscaledDeltaTime);

            if (_sessionSeq != sessionSeq) return; // 재접속 등으로 다음 세션으로 넘어갔다 - 남은 타이머 무시

            if (_reconnectToken == null)
            {
                Debug.LogWarning("[Session] 티켓 미수신 - 서버 재접속 스위치가 꺼져 있을 수 있음, 레거시 모드");
            }
        }

        // === 시스템 패킷 핸들러 (JobWorker 큐에서 실행) ===

        /// <summary>
        /// S_SessionTicket - 재접속 토큰/안정 플레이어 식별자/만료 시각 저장.
        /// 티켓이 오면 자기 식별자가 SessionID에서 StablePlayerId로 바뀌지만 기존 동기화 로직은 그대로 두고 저장만 한다
        /// </summary>
        private void HandleSessionTicket(ReadOnlyMemory<byte> data)
        {
            SessionTicketPacket packet = PacketSerializer.Deserialize<SessionTicketPacket>(data);

            _reconnectToken = packet.ReconnectToken;
            _stablePlayerId = packet.StablePlayerId;
            _tokenExpiresAtMs = packet.TokenExpiresAtMs;

            Debug.Log($"[Session] 티켓 저장 - stableId={_stablePlayerId}, 만료(UTC ms)={_tokenExpiresAtMs}");
        }

        /// <summary>S_ReconnectResult - 재접속 결과를 대기 중인 시도에 전달하기 위해 저장한다</summary>
        private void HandleReconnectResult(ReadOnlyMemory<byte> data)
        {
            _pendingReconnectResult = PacketSerializer.Deserialize<ReconnectResultPacket>(data);
        }

        /// <summary>
        /// S_Heartbeat - 같은 TimestampMs로 C_Heartbeat을 에코해 연결 상태를 상호 확인한다.
        /// 서버 계약: 받은 C_Heartbeat을 그대로 S_Heartbeat으로 에코하므로 클라가 보낸 에코가 다시 도착한다.
        /// 값 단독 비교로는 프로브/에코를 구분할 수 없어 (다중 in-flight에서 무한 에코 루프) 이미 에코한
        /// TimestampMs 이력 집합으로 판정한다. 이력이 상한(EchoedHeartbeatHistoryCap = 프로브 주기 5s 기준 160초치)에
        /// 도달하면 이력 전체를 비운다 - 서버 에코는 우리 에코 직후 수 초 내 도착하므로 재도착 오판 위험은 실질적으로 없다.
        /// 기존 C_Ping 루프(Ping.cs)는 서버 감지/RTT 용도로 그대로 유지된다
        /// </summary>
        private void HandleHeartbeat(ReadOnlyMemory<byte> data)
        {
            HeartbeatPacket packet = PacketSerializer.Deserialize<HeartbeatPacket>(data);

            // 이미 에코한 타임스탬프 - 내가 보낸 에코가 되돌아온 것 중 하나 (무한 에코 루프 방지)
            if (_echoedHeartbeatTimestamps.Contains(packet.TimestampMs)) return;

            if (_echoedHeartbeatTimestamps.Count >= EchoedHeartbeatHistoryCap)
            {
                // 상한 도달 - 전체 이력을 비운다 (최신 창 밖의 절대 재도착 에코만 오판 가능, 위 주석 근거)
                _echoedHeartbeatTimestamps.Clear();
            }

            _echoedHeartbeatTimestamps.Add(packet.TimestampMs);

            HeartbeatPacket echoPacket = new()
            {
                TimestampMs = packet.TimestampMs, // 서버가 보낸 값 그대로 되돌린다
            };

            _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(echoPacket));
        }

        /// <summary>
        /// S_ReconnectSnapshot - 스냅샷 적용 파트 미구현(다음 작업)이므로 청크를 폐기한다.
        /// 청크마다 로그가 몰릴 수 있어 여기서는 로그를 남기지 않는다
        /// </summary>
        private void HandleReconnectSnapshot(ReadOnlyMemory<byte> data)
        {
            // 폐기 (의도적으로 아무 동작도 하지 않음)
        }
    }
}
