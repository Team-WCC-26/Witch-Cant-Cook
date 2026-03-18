using System.Buffers;

namespace Protocol
{
    public class PacketPackageInfo
    {
        //public int Length { get; set; }
        public ushort Id { get; set; }
        public ReadOnlySequence<byte> Body { get; set; }
    }
}
