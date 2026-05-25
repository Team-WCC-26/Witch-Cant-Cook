using MemoryPack;
using System.Buffers;

namespace Protocol;

public static class PacketSerializer
{
    public static byte[] Serialize<T>(T packet, bool isServerPacket = false) where T : class
    {
        byte[] data = MemoryPackSerializer.Serialize(packet);

        int size = data.Length;

        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        ushort ushortId = (ushort)PacketTypeRegistry.GetId(packet.GetType(), isServerPacket);

        writer.Write(size);
        writer.Write(ushortId);
        writer.Write(data);

        return ms.ToArray();
    }

    public static T Deserialize<T>(byte[] buffer)
    {
        return MemoryPackSerializer.Deserialize<T>(buffer);
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> buffer)
    {
        return MemoryPackSerializer.Deserialize<T>(buffer);
    }

    public static T Deserialize<T>(ReadOnlyMemory<byte> buffer)
    {
        return MemoryPackSerializer.Deserialize<T>(buffer.Span);
    }

    public static T Deserialize<T>(ReadOnlySequence<byte> buffer)
    {
        return MemoryPackSerializer.Deserialize<T>(buffer);
    }
}
