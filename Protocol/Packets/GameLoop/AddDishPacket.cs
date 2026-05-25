using MemoryPack;
using System;
using System.Collections.Generic;
using System.Text;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.S_AddDish)]
public partial class AddDishPacket
{
    public int DishId { get; set; }
}
