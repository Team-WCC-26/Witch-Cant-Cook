using MemoryPack;
using System.Numerics;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_IngredientSpawn)]
[PacketId(PacketId.S_IngredientSpawn)]
public partial class IngredientSpawnPacket
{
    public long EntityId { get; set; }
    public int IngredientID { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Quaternion { get; set; }
}
