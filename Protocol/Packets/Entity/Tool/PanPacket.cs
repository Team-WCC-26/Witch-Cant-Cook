using MemoryPack;
using System.Numerics;

namespace Protocol;

// discarded
[MemoryPackable]
[PacketId(PacketId.C_PanStoveExit)]
[PacketId(PacketId.S_PanStoveExit)]
public partial class PanStoveExitPacket
{
    public long PanEntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.S_PanEject)]
public partial class PanEjectPacket
{
    public long PanEntityId { get; set; }
    public long IngredientEntityId { get; set; }
    public Vector3 Position { get; set; } // 정확한 프라이팬위치를 계산해서 날릴거면 Rotation값도 추가해야할것임
    public int EjectSeed { get; set; }
}
