using MemoryPack;
using System.Numerics;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_ToolSpawn)]
[PacketId(PacketId.S_ToolSpawn)]
public partial class ToolSpawnPacket
{
    public long EntityId { get; set; }
    public int ToolId { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Quaternion { get; set; }
}
