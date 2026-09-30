using MemoryPack;

namespace Protocol;

// 서버가 재료 위치를 알 수 없으므로 각 플레이어에게 재료와의 거리 측정을 요청한다
[MemoryPackable]
[PacketId(PacketId.S_IngredientDistanceRequest)]
public partial class IngredientDistanceRequestPacket
{
    public long EntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_IngredientDistanceResponse)]
public partial class IngredientDistanceResponsePacket
{
    public long EntityId { get; set; }
    public float Distance { get; set; }
}
