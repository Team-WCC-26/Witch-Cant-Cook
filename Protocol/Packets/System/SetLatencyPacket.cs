using MemoryPack;

namespace Protocol;

/// <summary>
/// 테스트용 세션 수신/송신 지연 설정 (LatencySimulator)
/// </summary>
[MemoryPackable]
[PacketId(PacketId.C_SetLatency)]
public partial class SetLatencyPacket
{
    public int ReceiveDelayMs { get; set; }
    public int SendDelayMs { get; set; }
}
