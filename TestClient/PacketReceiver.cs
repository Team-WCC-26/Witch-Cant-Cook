using MemoryPack;
using Protocol;
using System.Net.Sockets;

namespace TestClient;

public class PacketReceiver
{
    private const int HEADER_SIZE = 6;
    private const int DEFAULT_SIZE = 4096;
    private const int MAX_PACKET_SIZE = 65536;

    private byte[] _buffer = new byte[DEFAULT_SIZE];
    private int _readPos = 0;
    private int _writePos = 0;

    public Action<string> OnRoomCreated;

    public async Task StartAsync(NetworkStream stream)
    {
        while (true)
        {
            EnsureCapacity(1024);

            int bytesRead = await stream.ReadAsync(_buffer, _writePos, _buffer.Length - _writePos);

            if (bytesRead == 0) break;

            _writePos += bytesRead;

            Parse();
        }
    }

    private void Parse()
    {
        while (true)
        {
            int dataSize = _writePos - _readPos;

            if (dataSize < HEADER_SIZE) return;

            int length = BitConverter.ToInt32(_buffer, _readPos);
            ushort id = BitConverter.ToUInt16(_buffer, _readPos + 4);

            if (length > MAX_PACKET_SIZE)
            {
                throw new Exception($"Invalid Packet Size: {length}");
            }

            if (dataSize < length + 6) return;

            var body = new ReadOnlySpan<byte>(_buffer, _readPos + HEADER_SIZE, length);

            HandlePacket(id, body);

            _readPos += length + 6;
        }
    }

    private void HandlePacket(ushort id, ReadOnlySpan<byte> body)
    {
        switch ((PacketId)id)
        {
            case PacketId.S_GetRoom:
                var getRoomPacket = MemoryPackSerializer.Deserialize<GetRoomPacket>(body);

                Console.WriteLine("Current Rooms : " + string.Join(", ", getRoomPacket.RoomDatas.Select(x => x.Id)));
                break;

            case PacketId.S_JoinRoom:
                var joinRoomPacket = MemoryPackSerializer.Deserialize<JoinRoomPacket>(body);

                if (joinRoomPacket.RoomId == "F")
                {
                    Console.WriteLine($"Room Is Full");
                    break;
                }
                
                if (joinRoomPacket.RoomId == "N")
                {
                    Console.WriteLine($"Room Is Null");
                    break;
                }

                Program.IsJoinedRoom = true;
                OnRoomCreated?.Invoke(joinRoomPacket.RoomId);
                Console.WriteLine($"You Joined Number {joinRoomPacket.RoomId} Room.");
                break;

            case PacketId.S_PlayerEnter:
                var playerEnterPacket = MemoryPackSerializer.Deserialize<PlayerEnterPacket>(body);
                Console.WriteLine($"{playerEnterPacket.NewPlayerID} Joined Room");
                break;

            case PacketId.S_PlayerLeave:
                var playerLeavePacket = MemoryPackSerializer.Deserialize<PlayerLeavePacket>(body);
                Console.WriteLine($"{playerLeavePacket.PlayerID} Leave Room");
                break;

            case PacketId.S_ChatMessage:
                var chatPacket = MemoryPackSerializer.Deserialize<ChatMessagePacket>(body);
                Console.WriteLine($"{chatPacket.Sender} : {chatPacket.Message}");
                //LoadTester.GetChat(chatPacket);
                break;

            case PacketId.S_Notification:
                var notificationPacket = MemoryPackSerializer.Deserialize<RoomNotificationPacket>(body);
                Console.WriteLine(notificationPacket.Message);
                break;
        }
    }

    private void EnsureCapacity(int requiredSize)
    {
        int freeSize = _buffer.Length - _writePos;

        if (freeSize >= requiredSize) return;

        int dataSize = _writePos - _readPos;

        if (dataSize > 0)
        {
            Buffer.BlockCopy(_buffer, _readPos, _buffer, 0, dataSize);
        }

        _readPos = 0;
        _writePos = dataSize;

        freeSize = _buffer.Length - _writePos;

        if (freeSize >= requiredSize) return;

        int newSize = _buffer.Length * 2;

        while (newSize < dataSize + requiredSize)
        {
            newSize *= 2;
        }

        if (newSize > MAX_PACKET_SIZE)
        {
            throw new Exception("Buffer overflow (too large)");
        }

        byte[] newBuffer = new byte[newSize];
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, dataSize);

        _buffer = newBuffer;
    }
}