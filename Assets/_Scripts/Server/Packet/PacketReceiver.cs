using MemoryPack;
using System.Net.Sockets;
using System;
using Protocol;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Server
{
    public class PacketReceiver
    {
        public bool IsEnterRoom => _bEnterRoom;
        private bool _bEnterRoom = false;

        private const int HEADER_SIZE = 6;
        private const int DEFAULT_SIZE = 4096;
        private const int MAX_PACKET_SIZE = 65536;

        private byte[] _buffer = new byte[DEFAULT_SIZE];
        private int _readPos = 0;
        private int _writePos = 0;

        public async UniTaskVoid StartAsync(NetworkStream stream, CancellationToken token)
        {
            try
            {
                while (true)
                {
                    EnsureCapacity(1024);

                    int bytesRead = await stream.ReadAsync(_buffer, _writePos, _buffer.Length - _writePos, token);

                    if (bytesRead == 0) break;

                    _writePos += bytesRead;

                    Parse();
                }
            }
            finally
            {
                stream.Close();
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

                byte[] packet = new byte[length];
                Array.Copy(_buffer, _readPos + HEADER_SIZE, packet, 0, length);
                var body = new ReadOnlyMemory<byte>(packet);

                ServerManager.Instance.DispatchPacket((PacketId)id, body);

                //HandlePacket(id, body);

                _readPos += length + 6;
            }
        }

        private void HandlePacket(ushort id, ReadOnlySpan<byte> body)
        {
            switch ((PacketId)id)
            {
                case PacketId.S_GetRoom:
                    var getRoomPacket = MemoryPackSerializer.Deserialize<GetRoomPacket>(body);
                    //Debug.Log("Current Rooms : " + string.Join(", ", getRoomPacket.RoomIds));
                    break;

                case PacketId.S_JoinRoom:
                    var joinRoomPacket = MemoryPackSerializer.Deserialize<JoinRoomPacket>(body);
                    //_chatTxt.text = $"You Joined Number {joinRoomPacket.RoomId} Room.";
                    _bEnterRoom = true;
                    break;

                case PacketId.S_ChatMessage:
                    var chatPacket = MemoryPackSerializer.Deserialize<ChatMessagePacket>(body);
                    //_chatTxt.text += $"\n{chatPacket.Sender} : {chatPacket.Message}";
                    break;

                case PacketId.S_Notification:
                    var notificationPacket = MemoryPackSerializer.Deserialize<RoomNotificationPacket>(body);
                    //_chatTxt.text += "\n" + notificationPacket.Message;
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

}