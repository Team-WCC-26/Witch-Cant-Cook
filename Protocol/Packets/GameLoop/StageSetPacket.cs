using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_StageSet)]
[PacketId(PacketId.S_StageSet)]
public partial class StageSetPacket
{
    public int StageNum { get; set; }
}
