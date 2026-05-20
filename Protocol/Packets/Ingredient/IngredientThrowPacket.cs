using MemoryPack;
using System.Numerics;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_IngredientThrow)]
[PacketId(PacketId.S_IngredientThrow)]
public partial class IngredientThrowPacket
{
    public long EntityId;
    public Vector3 Position;
    public Vector3 Velocity;
}
