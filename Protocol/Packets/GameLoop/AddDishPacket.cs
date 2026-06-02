using MemoryPack;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_AddDish)]
public partial class AddDishPacket
{
    public int DishId { get; set; }
}
