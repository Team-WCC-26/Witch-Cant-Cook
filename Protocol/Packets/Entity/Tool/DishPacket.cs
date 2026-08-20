using MemoryPack;

namespace Protocol;

//[MemoryPackable]
//[PacketId(PacketId.S_AddDish)]
//public partial class AddDishPacket
//{
//    public int DishId { get; set; }
//}

[MemoryPackable]
public partial class DishStatePacket
{
    public int RecipeId { get; set; }
    public DishState State { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_ServeDish)]
[PacketId(PacketId.S_ServeDish)]
public partial class ServeDishPacket
{
    public long EntityId { get; set; }
}

[MemoryPackable]
[PacketId(PacketId.C_ClearDish)]
[PacketId(PacketId.S_ClearDish)]
public partial class ClearDishPacket
{
    public long EntityId { get; set; }
}

public enum DishState
{
    None = 0,
    Order = 1,
    Success = 2,
    Fail = 3,
}
