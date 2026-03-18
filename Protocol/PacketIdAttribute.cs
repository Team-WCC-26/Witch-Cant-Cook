using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Protocol;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class PacketIdAttribute : Attribute
{
    public PacketId PacketId { get; set; }

    public PacketIdAttribute(PacketId packetId)
    {
        PacketId = packetId;
    }
}
