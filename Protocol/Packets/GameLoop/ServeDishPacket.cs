using MemoryPack;
using System;
using System.Collections.Generic;
using System.Text;

namespace Protocol;

[MemoryPackable]
[PacketId(PacketId.C_ServeDish)]
[PacketId(PacketId.S_ServeDish)]
public partial class ServeDishPacket
{
    public long EntityId { get; set; }
    public bool Success { get; set; }
}
