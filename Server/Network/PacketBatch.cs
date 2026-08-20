using System.Buffers;

namespace Server;

public class PacketBatch
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public void Add(ReadOnlyMemory<byte> packet)
    {
        _buffer.Write(packet.Span);
    }

    public ReadOnlyMemory<byte> Build()
    {
        return _buffer.WrittenMemory;
    }

    public void Clear()
    {
        _buffer.Clear();
    }
}
