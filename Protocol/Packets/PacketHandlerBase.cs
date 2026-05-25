using MemoryPack;
using System.Buffers;

namespace Protocol;

public class PacketHandlerBase
{
    public static T DeSerialize<T>(ReadOnlySequence<byte> buffer) where T : class
    {
        return PacketSerializer.Deserialize<T>(buffer);
    }
}
