using MemoryPack;

namespace Protocol;

/// <summary>
/// 퇴장 트리거 오브젝트의 공유 상태 (플레이어별 상태가 아닌 오브젝트 1개 기준)
/// </summary>
public enum LeaveRoomTriggerState
{
    Idle,   // 대기 (펀치 시 활성화)
    Active, // 활성화 (10초 내 재펀치하면 펀치한 플레이어가 퇴장, 만료되면 자동으로 Idle 복귀)
}

/// <summary>
/// 퇴장 트리거 상태 동기화 패킷 (상태 전환 브로드캐스트/참가 응답 공용)
/// </summary>
[MemoryPackable]
[PacketId(PacketId.S_LeaveRoomTriggerState)]
public partial class LeaveRoomTriggerStatePacket
{
    public long TriggerEntityId { get; set; }
    public LeaveRoomTriggerState State { get; set; }
    public long RemainingMs { get; set; }              // Active 복귀 타이머 남은 시간 (Idle이면 0)
    public string? EjectedPlayerId { get; set; }       // 활성화 상태에서 펀치해 퇴장된 플레이어 (퇴장이 아니면 null)
}
