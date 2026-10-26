using Protocol;

namespace Server;

public class SystemHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_Ping)]
    public static void PingPong(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<PingPongPacket>(package.Body);

        // 현재 바인딩 세션이 아닌 세션의 핑은 무시한다 (stale 펜싱)
        if (!ReferenceEquals(session.Player.Session, session)) return;

        session.Player.LastPingTime = TimeUtil.NowMs();

        // S_Pong은 제어 패킷이므로 연결 상태와 무관하게 즉시 응답한다
        session.Player.SendControl(PacketSerializer.Serialize(packet, true));
    }

    [PacketHandler(PacketId.C_PingResult)]
    public static void GetPing(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<PingResultPacket>(package.Body);

        // 현재 바인딩 세션이 아닌 세션의 측정 결과는 무시한다 (stale 펜싱)
        if (!ReferenceEquals(session.Player.Session, session)) return;

        session.Player.Ping = packet.Ping;
    }

    [PacketHandler(PacketId.C_SetLatency)]
    public static void SetLatency(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<SetLatencyPacket>(package.Body);

        if (!LatencySimulator.TrySetLatency(session, packet.ReceiveDelayMs, packet.SendDelayMs))
        {
            Console.WriteLine($"[LatencySimulator] invalid latency value (session={session.SessionID}): receive={packet.ReceiveDelayMs}ms, send={packet.SendDelayMs}ms");
            return;
        }
    }

    [PacketHandler(PacketId.C_SessionHello)]
    public static void SessionHello(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<SessionHelloPacket>(package.Body);

        // 토큰 발급/티켓 응답은 레지스트리가 담당한다
        ServerContext.Instance.SessionRegistry.OnHello(session, packet);
    }

    [PacketHandler(PacketId.C_ReconnectRequest)]
    public static void ReconnectRequest(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<ReconnectRequestPacket>(package.Body);

        // 토큰 검증/재바인딩 작업 투입은 레지스트리가 담당한다
        ServerContext.Instance.SessionRegistry.OnReconnectRequest(session, packet);
    }

    [PacketHandler(PacketId.C_SnapshotApplied)]
    public static void SnapshotApplied(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<SnapshotAppliedPacket>(package.Body);

        // Live 전환/pending flush는 룸 샤드 작업으로 수행한다
        ServerContext.Instance.SessionRegistry.OnSnapshotApplied(session, packet);
    }

    [PacketHandler(PacketId.C_Heartbeat)]
    public static void Heartbeat(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<HeartbeatPacket>(package.Body);

        // 수신된 하트비트를 그대로 에코해 연결 상태를 상호 확인한다
        // S_Heartbeat은 제어 패킷이므로 연결 상태와 무관하게 즉시 응답한다
        session.Player.SendControl(PacketSerializer.Serialize(packet, true));
    }

    [PacketHandler(PacketId.C_Disconnect)]
    public static void Disconnect(Session session, PacketPackageInfo package)
    {
        // 토큰 즉시 무효화 + 최종 정리 작업 투입 + 세션 종료
        ServerContext.Instance.SessionRegistry.HandleExplicitDisconnect(session);
    }
}
