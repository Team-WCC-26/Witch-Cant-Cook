using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_GetRoom)]
[PacketId(PacketId.S_GetRoom)]
public partial class GetRoomPacket : IPacket
{
    public List<int> RoomIds { get; set; } = new();
}
