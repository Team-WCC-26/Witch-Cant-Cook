using MemoryPack;
using System.Buffers;

namespace Protocol
{
    public static class PacketSerializer
    {
        public static byte[] Serialize<T>(T packet) where T : IPacket
        {
            byte[] data = MemoryPackSerializer.Serialize(packet);

            int size = data.Length; // size + id 포함

            using MemoryStream ms = new();
            using BinaryWriter writer = new(ms);

            writer.Write(size);
            writer.Write((ushort)101);
            writer.Write(data);

            return ms.ToArray();
        }
    }
}
