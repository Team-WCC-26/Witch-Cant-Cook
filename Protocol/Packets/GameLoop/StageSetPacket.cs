using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_StageSet)]
[PacketId(PacketId.S_StageSet)]
public partial class StageSetPacket
{
    public int StageNum { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_StageStart)]
[PacketId(PacketId.S_StageStart)]
public partial class StageStartPacket
{
    public int StageNum { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_StageStop)]
[PacketId(PacketId.S_StageStop)]
public partial class StageStopPacket
{
}
