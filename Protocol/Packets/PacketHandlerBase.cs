using MemoryPack;
using System.Buffers;

namespace Protocol
{
    public class PacketHandlerBase<T>
    {
        public T DeSerialize(ReadOnlySequence<byte> buffer)
        {
            return MemoryPackSerializer.Deserialize<T>(buffer);
        }
    }
}
