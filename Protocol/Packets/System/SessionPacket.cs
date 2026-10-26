using MemoryPack;

namespace Protocol;

/// <summary>
/// 세션 협상 기능 플래그 (Capabilities 필드에 비트 조합으로 담긴다)
/// </summary>
[Flags]
public enum SessionCapability
{
    None = 0,
    Reconnect = 1 << 0,        // 토큰 기반 재접속 지원
    ServerHeartbeat = 1 << 1,  // 서버 주도 하트비트 프로브 지원
}

/// <summary>
/// 재접속 실패 사유
/// </summary>
public enum ReconnectFailureReason
{
    None = 0,
    InvalidToken,   // 토큰에 해당하는 세션이 없음
    Expired,        // 토큰 만료 또는 유예 시간 경과
    SessionActive,  // 다른 세션이 이미 활성 바인딩 중
    ServerBusy,     // 서버 측 사유로 재접속 불가
}

/// <summary>
/// 클라이언트 → 서버 세션 협상 (연결 직후 1회)
/// </summary>
[MemoryPackable]
[PacketId(PacketId.C_SessionHello)]
public partial class SessionHelloPacket
{
    public int ProtocolVersion { get; set; }
    public int Capabilities { get; set; } // SessionCapability 비트 조합
    public long RequestId { get; set; }
}

/// <summary>
/// 서버 → 클라이언트 세션 티켓 (Hello 응답)
/// <para/> ReconnectToken은 이 응답으로 1회만 전달되며 서버는 해시만 보관한다
/// </summary>
[MemoryPackable]
[PacketId(PacketId.S_SessionTicket)]
public partial class SessionTicketPacket
{
    public string ReconnectToken { get; set; }
    public string StablePlayerId { get; set; }
    public long TokenExpiresAtMs { get; set; } // UTC Unix ms
    public int ProtocolVersion { get; set; }
    public int Capabilities { get; set; } // SessionCapability 비트 조합 (서버 지원분)
}

/// <summary>
/// 클라이언트 → 서버 재접속 요청 (유예 시간 내 토큰으로 세션 복구)
/// </summary>
[MemoryPackable]
[PacketId(PacketId.C_ReconnectRequest)]
public partial class ReconnectRequestPacket
{
    public string ReconnectToken { get; set; }
    public long RequestId { get; set; }
}

/// <summary>
/// 서버 → 클라이언트 재접속 결과
/// <para/> Accepted가 true면 이어서 S_ReconnectSnapshot 청크가 전송된다
/// </summary>
[MemoryPackable]
[PacketId(PacketId.S_ReconnectResult)]
public partial class ReconnectResultPacket
{
    public bool Accepted { get; set; }
    public ReconnectFailureReason FailureReason { get; set; }
    public string StablePlayerId { get; set; }
    public string RoomId { get; set; }
    public int BindingGeneration { get; set; }
    public long SnapshotId { get; set; }
}

/// <summary>
/// 클라이언트 → 서버 스냅샷 적용 완료 (ACK)
/// <para/> ACK 전 서버는 증분 패킷을 보류하고, ACK 후 일괄 전송하며 기존 증분 동기화로 복귀한다
/// </summary>
[MemoryPackable]
[PacketId(PacketId.C_SnapshotApplied)]
public partial class SnapshotAppliedPacket
{
    public long SnapshotId { get; set; }
}

/// <summary>
/// 하트비트 (C_Heartbeat 수신 시 S_Heartbeat로 에코, 협상 세션에는 서버가 주기적으로 S_Heartbeat 프로브 전송)
/// </summary>
[MemoryPackable]
[PacketId(PacketId.C_Heartbeat)]
[PacketId(PacketId.S_Heartbeat)]
public partial class HeartbeatPacket
{
    public long TimestampMs { get; set; }
}
