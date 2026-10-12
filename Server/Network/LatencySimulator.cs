using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Protocol;
using SuperSocket.Connection;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Session;

namespace Server;

/// <summary>
/// 테스트용 지연 시뮬레이터.
/// 세션별로 수신(RX)/송신(TX) 지연(ms)을 독립 적용해 동기화 문제를 재현한다.
/// 지연이 설정된 세션의 패킷은 소유 복사 후 전용 큐를 거친다. (지연 0이어도 큐 경유로 enqueue 순서=FIFO 보장)
/// 미설정 세션은 기존 직접 경로(PacketDispatcher.Dispatch / Session.SendAsync)를 그대로 유지한다.
/// </summary>
public static class LatencySimulator
{
    // 지연 상한(ms)
    public const int MaxDelayMs = 60000;

    // 방향별 큐 개수 상한: 최대 지연 60초 + 틱(50ms)당 월드 상태 배치 스트림에서 포화되지 않는 여유값
    private const int MaxQueueCount = 4096;

    // 방향별 큐 바이트 상한: 소형 패킷 다수로 개수 상한만 회피하는 경우를 잡는다
    private const int MaxQueueBytes = 16 * 1024 * 1024;

    private static readonly ConcurrentDictionary<string, SimulatedSession> _sessions = new();

    // 세션 상태 lifecycle(생성(지연 완전 설정)→등록→소비자 시작 / 제거)을 전역으로 원자화하는 lock
    // 동시 TrySetLatency의 중복 생성/덮어쓰기와 RemoveAsync 이후 재생성 누수를 막는다
    private static readonly object _lifecycleLock = new();

    /// <summary>
    /// 세션의 수신/송신 지연을 설정한다. (음수 또는 상한 초과, 닫힌/제거 중 세션은 false, 예외 없음)
    /// 신규 생성 시 지연값이 완전히 설정된 상태로 만들어 등록한다. (공개 직후 다른 RX/TX가 지연 0으로 직행 우회 방지)
    /// </summary>
    public static bool TrySetLatency(IAppSession session, int receiveDelayMs, int sendDelayMs)
    {
        if (receiveDelayMs is < 0 or > MaxDelayMs) return false;
        if (sendDelayMs is < 0 or > MaxDelayMs) return false;

        SimulatedSession sim;

        // 전역 lifecycle lock: 닫힘 검사→생성(지연 완전 설정)→등록→소비자 시작을 원자화한다.
        // RemoveAsync와의 경합에서 제거된 세션 상태가 재생성되어 고아화(누수)되지 않게 한다.
        lock (_lifecycleLock)
        {
            // 닫힌 세션에는 상태를 만들지 않는다.
            // (SuperSocket은 OnSessionClosedAsync 호출 전에 State를 Closed로 설정하므로,
            //  RemoveAsync 완료 후 도착한 C_SetLatency는 여기서 실패 처리된다)
            if (session.State == SessionState.Closed) return false;

            sim = GetOrCreate(session, receiveDelayMs, sendDelayMs, out bool existing);

            // 제거 경합 중인 상태에는 지연을 적용하지 않는다
            if (sim.Stopping) return false;

            // ApplyDelay는 기존 상태 갱신 경로에만 사용한다 (신규 생성분은 생성자에서 이미 설정됨)
            if (existing)
            {
                lock (sim.Lock)
                {
                    ApplyDelay(sim.Receive, receiveDelayMs);
                    ApplyDelay(sim.Send, sendDelayMs);
                }
            }

            // 등록이 확정된 상태에 한해 소비자 시작 (생성자에서 시작하지 않는다)
            sim.StartConsumers();
        }

        Console.WriteLine($"[LatencySimulator] latency set: session={session.SessionID}, receive={receiveDelayMs}ms, send={sendDelayMs}ms");

        return true;
    }

    /// <summary>
    /// 수신 경로 진입점. 지연 설정 세션은 항상 큐잉 후 FIFO dispatch (지연 0 포함),
    /// 미설정 세션은 기존 직접 dispatch.
    /// </summary>
    public static ValueTask ReceiveAsync(IAppSession session, PacketPackageInfo package)
    {
        SimulatedSession? sim = Find(session.SessionID, session);

        // 지연 미설정 세션(상태 없음): 기존 직접 경로
        // RemoveAsync 후 남은 경합 패킷은 닫힌 세션에 dispatch될 수 있으나, 기존 무지연 경로와 동일 성질이다.
        // 상태 없음 세션의 동시 호출 간 처리 순서도 기존 직접 경로와 동일 성질이다. (보장 없음)
        if (sim == null)
        {
            PacketDispatcher.Dispatch(session, (PacketId)package.Id, package);
            return ValueTask.CompletedTask;
        }

        DirectionState<ReceiveItem> dir = sim.Receive;
        bool overflow = false;

        // enqueue와 설정 변경을 같은 lock으로 선형화
        lock (sim.Lock)
        {
            if (sim.Stopping) return ValueTask.CompletedTask; // 제거 진행 중: 드랍

            if ((long)dir.PendingCount >= MaxQueueCount || (long)dir.PendingBytes + package.Body.Length > MaxQueueBytes)
            {
                // 큐 포화: 드랍 + 세션 종료 요청
                overflow = true;
            }
            else
            {
                // Dispatch 이후 원본 버퍼는 PipeReader Advance로 재사용되므로 큐에는 소유 복사본만 보관
                byte[] body = new byte[package.Body.Length];
                package.Body.CopyTo(body);

                // 항상 큐 경유: enqueue 시점 순서가 곧 dispatch 순서 (지연 0이면 DueAt=now)
                ReceiveItem item = new(session, (PacketId)package.Id, body, TimeUtil.NowMs() + dir.DelayMs, body.Length);

                dir.PendingCount++;
                dir.PendingBytes += body.Length;

                // 큐 가득 시(TryWrite 실패)도 포화 취급
                if (!dir.Channel.Writer.TryWrite(item))
                {
                    dir.PendingCount--;
                    dir.PendingBytes -= body.Length;
                    overflow = true;
                }
            }
        }

        if (overflow)
        {
            Console.WriteLine($"[LatencySimulator] receive queue overflow, drop packet (session={session.SessionID})");
            return RequestCloseAsync(session);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 송신 경로 진입점. 지연 설정 세션은 항상 큐잉 후 FIFO 전송 (지연 0 포함),
    /// 미설정 세션은 기존 직접 전송.
    /// 반환 ValueTask는 큐 등록(또는 직접 전송) 완료까지를 의미하고 큐 경유 시 실제 전송 완료는 소비자가 관찰한다.
    /// 모든 호출부가 반환값을 기다리지 않으므로 큐 경유 시 전송 완료가 호출자보다 늦어질 수 있다. (게임 로직은 완료 대기 없음)
    /// </summary>
    public static ValueTask SendAsync(IAppSession session, ReadOnlyMemory<byte> packet)
    {
        SimulatedSession? sim = Find(session.SessionID, session);

        // 지연 미설정 세션(상태 없음): 기존 직접 경로 (복사/큐 오버헤드 없음)
        // RemoveAsync 후 남은 경합 패킷은 닫힌 세션에 전송될 수 있으나, 기존 무지연 경로와 동일 성질이다.
        // 상태 없음 세션의 동시 호출 간 실제 전송 순서도 기존 직접 경로와 동일 성질이다. (보장 없음)
        if (sim == null) return session.SendAsync(packet);

        // 상태 세션은 지연이 0이어도 항상 큐 경유: enqueue 시점 순서가 곧 전송 순서 (결정론적 FIFO)
        bool overflow = false;

        // enqueue와 설정 변경을 같은 lock으로 선형화
        lock (sim.Lock)
        {
            if (sim.Stopping) return ValueTask.CompletedTask; // 제거 진행 중: 드랍

            if (!TryEnqueueSend(sim, packet))
            {
                // 큐 포화: 새 패킷은 버림 (게임 로직/Room.Tick 절대 블로킹 금지)
                overflow = true;
            }
        }

        if (overflow)
        {
            Console.WriteLine($"[LatencySimulator] send queue overflow, drop packet (session={session.SessionID})");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 세션 종료 시 지연 시뮬 상태를 정리한다. (동일 SessionID 재사용 대비 identity 검사)
    /// lifecycle lock 안에서 identity 검증→Stopping 표시→등록 해제→취소→채널 완료→깨움까지 원자 수행하고,
    /// 소비자 Task 대기와 자원 해제는 lock 밖에서 수행한다. (상태 세션은 항상 큐 경유이므로 소비자만 대기하면 된다)
    /// </summary>
    public static async ValueTask RemoveAsync(string sessionId, IAppSession session)
    {
        SimulatedSession? sim;

        lock (_lifecycleLock)
        {
            if (!_sessions.TryGetValue(sessionId, out var existing) || !ReferenceEquals(existing.Session, session))
            {
                // 지연 미설정 세션(상태 없음) 또는 ID 재사용으로 다른 세션: 정리 대상 아님
                return;
            }

            ShutdownSessionState(sessionId, existing);
            sim = existing;
        }

        await CleanupSessionStateAsync(sessionId, sim);
    }

    #region 내부 헬퍼

    // 동일 세션 객체(identity)가 보유한 상태만 반환
    private static SimulatedSession? Find(string sessionId, IAppSession session)
    {
        if (_sessions.TryGetValue(sessionId, out var sim) && ReferenceEquals(sim.Session, session))
        {
            return sim;
        }

        return null;
    }

    // 지연 설정 세션을 위한 상태 생성/재사용 — 반드시 _lifecycleLock 보호 하에 호출한다.
    // 신규 생성은 지연값이 완전히 설정된 상태로 만들어 등록한다: 등록(공개) 직후에도 다른 RX/TX가
    // 지연 0으로 직행 우회하지 않도록 공개된 상태는 처음부터 완전해야 한다. (기존 상태 재사용은 existing=true)
    private static SimulatedSession GetOrCreate(IAppSession session, int receiveDelayMs, int sendDelayMs, out bool existing)
    {
        if (_sessions.TryGetValue(session.SessionID, out var registeredSession))
        {
            if (ReferenceEquals(registeredSession.Session, session))
            {
                existing = true;
                return registeredSession;
            }

            // SessionID 재사용(GUID라 사실상 없지만 안전망): 기존 상태를 정리하고 새 상태로 교체한다.
            // lifecycle lock 안에서 원자 수행, 기존 소비자는 취소 신호로 자체 종료한 뒤 별도 정리된다.
            ShutdownSessionState(session.SessionID, registeredSession);
            _ = CleanupSessionStateAsync(session.SessionID, registeredSession);
        }

        SimulatedSession sim = new(session, receiveDelayMs, sendDelayMs);

        if (!_sessions.TryAdd(session.SessionID, sim))
        {
            // 극한 동시 생성 경쟁(lifecycle lock으로 도달 불가, 방어막): 기존에 등록된 상태를 사용한다.
            // 신규 생성분은 소비자 시작 전이므로 예약 자원(CTS/타이머)만 해제하면 끝난다.
            // 절대 indexer로 기존 상태를 덮어쓰지 않는다(기존 소비자/타이머 고아화 방지).
            SimulatedSession registered = _sessions[session.SessionID];
            sim.Discard();
            existing = true;
            return registered;
        }

        existing = false;
        return sim;
    }

    // 세션 상태 종료 1단계 — 반드시 _lifecycleLock 보호 하에 호출한다.
    // Stopping 표시 → 등록 해제 → 취소 → 채널 완료 → 깨움 (이후 모든 경합 enqueue는 차단된다)
    private static void ShutdownSessionState(string sessionId, SimulatedSession sim)
    {
        sim.Stopping = true;
        _sessions.TryRemove(KeyValuePair.Create(sessionId, sim));

        // 취소 + 큐 완료 → 채널 대기 중인 소비자 즉시 해제
        sim.Cts.Cancel();
        sim.Receive.Channel.Writer.TryComplete();
        sim.Send.Channel.Writer.TryComplete();

        // 도래 대기(타이머 재움) 중인 소비자 즉시 깨움
        sim.Receive.WakeNow();
        sim.Send.WakeNow();
    }

    // 세션 상태 종료 2단계 — _lifecycleLock 밖에서 소비자 종료 대기 후 자원 해제한다.
    // lock 보유 중 await하지 않는다: CTS 취소 콜백이 동기 실행되어도 lock 역전/교착이 없다.
    private static async Task CleanupSessionStateAsync(string sessionId, SimulatedSession sim)
    {
        try
        {
            // 방향별 소비자 종료까지 대기 (상태 세션은 항상 큐 경유이므로 소비자 종료만 확인하면 된다)
            await Task.WhenAll(sim.Receive.ConsumerTask, sim.Send.ConsumerTask);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LatencySimulator] consumer stop error (session={sessionId}): {ex.Message}");
        }

        sim.Cts.Dispose();
        sim.Receive.WakeTimer.Dispose();
        sim.Send.WakeTimer.Dispose();
    }

    // TX 큐 인큐 — 반드시 sim.Lock 보유 중 호출 (포화 시 false: 게임 로직/Room.Tick 블로킹 없이 드랍)
    private static bool TryEnqueueSend(SimulatedSession sim, ReadOnlyMemory<byte> packet)
    {
        DirectionState<SendItem> dir = sim.Send;

        if ((long)dir.PendingCount >= MaxQueueCount || (long)dir.PendingBytes + packet.Length > MaxQueueBytes) return false;

        // 송신 원본은 호출자(월드 상태 배치 버퍼 등)가 재사용할 수 있어 큐에는 소유 복사본만 보관
        byte[] buffer = packet.ToArray();
        // 항목별 취소 토큰을 보관: 실제 전송 시 세션 제거/정체로 CTS가 취소되면 전송 작업이 즉시 탈출한다
        SendItem item = new(sim.Session, buffer, TimeUtil.NowMs() + dir.DelayMs, buffer.Length, sim.Cts.Token);

        dir.PendingCount++;
        dir.PendingBytes += buffer.Length;

        // 큐 가득 시(TryWrite 실패)도 포화 취급
        if (!dir.Channel.Writer.TryWrite(item))
        {
            dir.PendingCount--;
            dir.PendingBytes -= buffer.Length;
            return false;
        }

        return true;
    }

    // 방향별 지연 적용 (기존 항목 DueAt은 유지, 0 전환 시 drain)
    private static void ApplyDelay<T>(DirectionState<T> dir, int delayMs) where T : QueuedItem
    {
        if (dir.DelayMs == delayMs) return;

        dir.DelayMs = delayMs;

        // 지연 0 전환: 대기 항목을 FIFO 유지한 채 남은 지연 없이 처리 / 양수 재설정 시 비활성화
        dir.Draining = delayMs == 0 && (dir.PendingCount > 0 || dir.Draining);

        // 변경 즉시 반영을 위해 대기 중 소비자를 깨움
        dir.WakeNow();
    }

    // 포화된 RX 세션 종료 요청 (드랍+로그 이후 서버 보호 목적)
    private static async ValueTask RequestCloseAsync(IAppSession session)
    {
        try
        {
            await session.CloseAsync(CloseReason.LocalClosing);
        }
        catch (Exception)
        {
            // 이미 닫힌 세션 등 종료 요청 실패는 무시
        }
    }

    // RX 큐 항목을 기존 dispatch 경로로 위임
    private static ValueTask DeliverReceiveItem(ReceiveItem item)
    {
        PacketDispatcher.Dispatch(item.Session, item.Id, new PacketPackageInfo()
        {
            Id = (ushort)item.Id,
            Body = new ReadOnlySequence<byte>(item.Body),
        });

        return ValueTask.CompletedTask;
    }

    // TX 큐 항목을 기존 session 전송 경로로 위임 (취소 토큰 전달: 세션 정체 시에도 CTS 취소로 탈출)
    private static ValueTask DeliverSendItem(SendItem item)
    {
        return item.Session.SendAsync(new ReadOnlyMemory<byte>(item.Buffer), item.Token);
    }

    // 방향별 전용 소비자 루프 (RX/TX 분리로 방향 독립성 유지)
    // 상태 세션의 유일한 송신자는 TX 소비자이므로 큐 FIFO 순서가 곧 실제 전송 순서다.
    private static async Task RunConsumerLoop<T>(SimulatedSession sim, DirectionState<T> dir, Func<T, ValueTask> deliver) where T : QueuedItem
    {
        ChannelReader<T> reader = dir.Channel.Reader;

        try
        {
            while (true)
            {
                // 빈 큐: 새 항목 도착까지 대기 (peek는 소비하지 않는다)
                if (!reader.TryPeek(out T? head))
                {
                    // 채널이 완료되면 소비자 종료
                    if (!await reader.WaitToReadAsync(sim.Cts.Token)) break;

                    // 도착 직후 경합 재확인 (완료로 빈 computed 큐 등)
                    if (!reader.TryPeek(out head)) continue;
                }

                // head 도래 여부 판정 (설정 변경과 같은 lock으로 선형화)
                Task? wakeTask = null;

                lock (sim.Lock)
                {
                    long remaining = head.DueAtMs - TimeUtil.NowMs();

                    // enqueue 시 고정된 DueAt만 본다 (이후 지연 변경은 기존 항목 DueAt 유지)
                    if (remaining > 0 && !dir.Draining)
                    {
                        var wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        dir.Wake = wake;
                        dir.WakeTimer.Change(remaining, Timeout.Infinite);
                        wakeTask = wake.Task;
                    }
                }

                if (wakeTask != null)
                {
                    try
                    {
                        // 남은 지연 동안 wake 신호 대기 (제거/설정 변경은 즉시 깨움, 세션 취소는 예외로 종료)
                        await wakeTask.WaitAsync(sim.Cts.Token);
                    }
                    finally
                    {
                        // 재확인 전 타이머 해제 (다음 대기 시 재사용)
                        dir.WakeTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    }

                    // 도래 여부 재확인 후 처리
                    continue;
                }

                // FIFO 인출 (SingleReader 소비자만 인출, 전달 전 단 한 번의 인출)
                head = await reader.ReadAsync(sim.Cts.Token);

                try
                {
                    // 전달 직전 재확인: 제거 중/세션 식별 불일치/닫힌 세션에는 오래된 패킷을 전달하지 않는다
                    if (!sim.Stopping && ReferenceEquals(sim.Session, head.Session) && head.Session.State != SessionState.Closed)
                    {
                        await deliver(head);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 종료 경합(전송 취소 포함): 상위 정상 종료 흐름으로 위임
                    throw;
                }
                catch (Exception ex)
                {
                    // 전달 실패는 삼키지 않고 로그 남김 (불가피 시 세션 종료 요청)
                    // RequestCloseAsync는 await하지 않는다(fire-and-forget): 소비자 스레드를 블로킹하지 않는다
                    Console.WriteLine($"[LatencySimulator] delivery error (session={sim.Session.SessionID}): {ex.Message}");
                    _ = RequestCloseAsync(head.Session);
                }
                finally
                {
                    // pending은 전달 완료까지 유지: 전달 중 신규 패킷도 큐 경유로 FIFO 보장
                    lock (sim.Lock)
                    {
                        dir.PendingCount--;
                        dir.PendingBytes -= head.Size;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료 (세션 제거)
        }
        catch (ChannelClosedException)
        {
            // 정상 종료 (세션 제거)
        }
        catch (ObjectDisposedException)
        {
            // 정상 종료 (세션 제거 경합)
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LatencySimulator] consumer loop error (session={sim.Session.SessionID}): {ex}");
        }
    }

    #endregion

    #region 지연 큐 상태

    // 지연 큐 항목 공통 (DueAt까지 대기 후 FIFO 처리)
    private abstract class QueuedItem
    {
        public readonly IAppSession Session;
        public readonly long DueAtMs;
        public readonly int Size; // 큐 바이트 상한 계산용

        protected QueuedItem(IAppSession session, long dueAtMs, int size)
        {
            Session = session;
            DueAtMs = dueAtMs;
            Size = size;
        }
    }

    // RX 큐 항목: 도래 시 기존 dispatch 경로로 위임
    private sealed class ReceiveItem : QueuedItem
    {
        public readonly PacketId Id;
        public readonly byte[] Body;

        public ReceiveItem(IAppSession session, PacketId id, byte[] body, long dueAtMs, int size)
            : base(session, dueAtMs, size)
        {
            Id = id;
            Body = body;
        }
    }

    // TX 큐 항목: 도래 시 기존 Session.SendAsync 경로로 위임
    private sealed class SendItem : QueuedItem
    {
        public readonly byte[] Buffer;
        public readonly CancellationToken Token; // 전송 취소용 (세션 제거/정체 시 CTS 취소로 탈출)

        public SendItem(IAppSession session, byte[] buffer, long dueAtMs, int size, CancellationToken token)
            : base(session, dueAtMs, size)
        {
            Buffer = buffer;
            Token = token;
        }
    }

    // 한 방향(RX/TX)의 공통 지연 큐 상태
    private abstract class DirectionBase
    {
        // 도래 알림용 재사용 타이머 (sleep/wake 신호)
        public readonly Timer WakeTimer;
        public Task ConsumerTask = null!; // StartConsumers에서 1회 설정 (등록 확정 전에는 null)

        public volatile int DelayMs; // enqueue 때 당시 값 사용 (공개 후 lock 없는 읽기 가시성 보장)
        public int PendingCount; // SimulatedSession lock 보호 (큐잉 + 전달 중 항목)
        public int PendingBytes; // SimulatedSession lock 보호
        public bool Draining;    // SimulatedSession lock 보호 (지연 0 전환 시 대기 항목 즉시 순차 처리)

        // 소비자가 잠들 때 만드는 wake 신호 (lock 내 생성, wake 시 1회 소비)
        public TaskCompletionSource? Wake;

        protected DirectionBase()
        {
            WakeTimer = new Timer(WakeTimerCallback, this, Timeout.Infinite, Timeout.Infinite);
        }

        // 잠든 소비자 깨우기 (신호는 1회 소비)
        public void WakeNow()
        {
            Interlocked.Exchange(ref Wake, null)?.TrySetResult();
        }

        private static void WakeTimerCallback(object? state)
        {
            // 도래 시그널: 대기 중인 소비자를 깨워 head 재확인
            (state! as DirectionBase)?.WakeNow();
        }
    }

    private sealed class DirectionState<T> : DirectionBase where T : QueuedItem
    {
        // bounded + 단일 소비자: FullMode.Wait는 TryWrite 사용 시 성공/실패만 보고 (포화 시 드랍 정책)
        public readonly Channel<T> Channel;

        public DirectionState()
        {
            Channel = System.Threading.Channels.Channel.CreateBounded<T>(new BoundedChannelOptions(MaxQueueCount)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }
    }

    // 세션별 지연 시뮬 상태 (지연이 설정된 세션만 생성, 두 값 모두 0이어도 종료 전까지 유지)
    private sealed class SimulatedSession
    {
        public readonly IAppSession Session;
        public readonly object Lock = new();                 // enqueue/설정 변경 선형화
        public readonly CancellationTokenSource Cts = new(); // 세션 전역 취소 (소비자 2개 공유)
        public readonly DirectionState<ReceiveItem> Receive = new();
        public readonly DirectionState<SendItem> Send = new();

        public volatile bool Stopping; // RemoveAsync 진행 중 (enqueue 차단, 오래된 패킷 폐기)

        public SimulatedSession(IAppSession session, int receiveDelayMs, int sendDelayMs)
        {
            Session = session;

            // 지연값은 등록 전에 완전히 설정한다: 공개된 상태는 처음부터 유효한 지연 상태여야 한다
            // (등록 직후 다른 RX/TX가 지연 0으로 직행 우회하지 않는다)
            Receive.DelayMs = receiveDelayMs;
            Send.DelayMs = sendDelayMs;

            // 소비자는 여기서 시작하지 않는다: 등록 확정 후 StartConsumers에서 시작한다
            // (등록 경쟁에서 진 상태를 폐기할 때 소비자 정리 비용이 없게 하기 위함)
        }

        // 등록 확정 후 소비자 시작 (lifecycle lock 보호 하에 호출, 재호출 무시)
        public void StartConsumers()
        {
            // RX/TX는 반드시 분리된 전용 소비자 작업 (방향 독립성)
            Receive.ConsumerTask ??= RunConsumerLoop(this, Receive, DeliverReceiveItem);
            Send.ConsumerTask ??= RunConsumerLoop(this, Send, DeliverSendItem);
        }

        // 소비자 시작 전 폐기(등록 경쟁 패자): 예약 자원(CTS/타이머)만 해제하면 끝난다
        public void Discard()
        {
            Cts.Dispose();
            Receive.WakeTimer.Dispose();
            Send.WakeTimer.Dispose();
        }
    }

    #endregion
}
