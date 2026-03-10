using SuperSocket.ProtoBase;
using System.Buffers;

namespace Protocol
{
    public class PacketPackageDecoder : IPackageDecoder<PacketPackageInfo>
    {
        public PacketPackageInfo Decode(ref ReadOnlySequence<byte> buffer, object context)
        {
            var reader = new SequenceReader<byte>(buffer);

            reader.TryReadLittleEndian(out int len);
            reader.TryReadLittleEndian(out ushort id);

            return new PacketPackageInfo()
            {
                Id = id,
                Body = buffer.Slice(6, len)
            };
        }
    }
}
