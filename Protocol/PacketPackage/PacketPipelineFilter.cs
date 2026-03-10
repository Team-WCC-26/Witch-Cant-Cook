using SuperSocket.ProtoBase;
using System.Buffers;

namespace Protocol
{
    public class PacketPipelineFilter : FixedHeaderPipelineFilter<PacketPackageInfo>
    {
        public PacketPipelineFilter() : base(6) // Length : 4 + Id : 2
        {
        }

        protected override int GetBodyLengthFromHeader(ref ReadOnlySequence<byte> buffer)
        {
            var reader = new SequenceReader<byte>(buffer);
            reader.TryReadLittleEndian(out int len);

            return len;
        }
    }
}
