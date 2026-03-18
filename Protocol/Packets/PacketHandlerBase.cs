using MemoryPack;
using System.Buffers;

namespace Protocol;

public class PacketHandlerBase
{
    public static T DeSerialize<T>(ReadOnlySequence<byte> buffer) where T : class, IPacket
    {
        return MemoryPackSerializer.Deserialize<T>(buffer);
    }
}
