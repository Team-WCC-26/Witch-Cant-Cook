using MemoryPack;
using System.Numerics;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_EntityThrow)]
[PacketId(PacketId.S_EntityThrow)]
public partial class EntityThrowPacket
{
    public long EntityId { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Velocity { get; set; }
}
