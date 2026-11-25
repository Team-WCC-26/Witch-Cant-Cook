using MemoryPack;
using System.Net.Sockets;
using System;
using Protocol;
using Cysharp.Threading.Tasks;
using System.Threading;
using System.Buffers;
using UnityEngine;

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

        // 마지막 서버 패킷 수신 시각(Stopwatch ms) - 수신 스레드(Parse)가 기록하고 SessionReconnector의
        // 수신 정지 워치독이 읽는다. long 판독의 원자성은 Volatile.Read/Write로 보장한다 (32비트 플랫폼 대비)
        public long LastReceiveTickMs => Volatile.Read(ref _lastReceiveTickMs);
        private long _lastReceiveTickMs = TimeUtil.NowMs();

        // 의도적 정리(TeardownConnection) 표시 - true면 이후의 어떤 종료 경로(토큰 취소, close 유발 예외 등)에서도
        // 끊김 콜백을 발화하지 않는다. Unity Mono BCL의 NetworkStream.ReadAsync는 취소 등록이 안 걸리고,
        // 스트림 close 시점에 IOException/ObjectDisposedException 경로로 빠지기 때문에 정리 측의 선(先) 표시가 필요하다
        public bool IsClosing
        {
            get => Volatile.Read(ref _isClosing) != 0;
            set => Volatile.Write(ref _isClosing, value ? 1 : 0);
        }
        private int _isClosing; // 0 = 활성, 1 = 정리 표시

        // 끊김 콜백 - ServerManager가 주입한다(SessionReconnector.NotifyConnectionLost).
        // 자기 인스턴스(this)를 함께 전달해 구(舊) 수신 태스크의 늦은 콜백이 현재 세대와 섞이지 않게 한다
        private Action<PacketReceiver> _onConnectionLost = null;

        // 끊김 콜백 1회 가드 - 수신 인스턴스당 최대 1회만 발화된다 (FIN/예외 이중 경로, 후속 예외 방지)
        private int _lossNotified = 0;

        public async UniTaskVoid StartAsync(NetworkStream stream, CancellationToken token, Action<PacketReceiver> onConnectionLost)
        {
            _onConnectionLost = onConnectionLost;
            _buffer = new byte[DEFAULT_SIZE];
            _readPos = 0;
            _writePos = 0;

            bool connectionLost = false;

            try
            {
                while (true)
                {
                    EnsureCapacity(1024);

                    int bytesRead = await stream.ReadAsync(_buffer, _writePos, _buffer.Length - _writePos, token);

                    if (bytesRead == 0)
                    {
                        // 정상 종료(FIN) - 서버가 먼저 연결을 닫았다. 종료 전에 끊김 콜백을 발행한다
                        connectionLost = true;
                        break;
                    }

                    _writePos += bytesRead;

                    Parse();
                }
            }
            catch (OperationCanceledException)
            {
                // 수신 전용 토큰이 취소됨 - 의도 종료이므로 끊김 콜백을 발행하지 않는다
            }
            catch (Exception ex)
            {
                if (IsClosing)
                {
                    // 의도 정리로 유발된 예외(IOException/ObjectDisposedException 포함) - 끊김 아님, 콜백 무음
                }
                else
                {
                    // 전송 예외(RST, 소켓 오류, 잘못된 패킷 크기 등) - 동일한 끊김 콜백을 발행한다 (무음 무전파 금지)
                    Debug.LogWarning($"[PacketReceiver] 수신 루프 예외 종료 - {ex.GetType().Name}: {ex.Message}");

                    connectionLost = true;
                }
            }
            finally
            {
                stream.Close();
            }

            // IsClosing이면 (파싱 예외든 close 유발 예외든) 콜백을 발화하지 않는다. 발화는 수신 인스턴스당 1회
            if (connectionLost && Interlocked.Exchange(ref _lossNotified, 1) == 0)
            {
                _onConnectionLost?.Invoke(this);
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

                // 마지막 수신 시각을 dispatch 직전마다 갱신한다 - 수신 정지 워치독의 판단 근거
                Volatile.Write(ref _lastReceiveTickMs, TimeUtil.NowMs());

                ServerManager.Instance.DispatchPacket((PacketId)id, body);

                _readPos += length + 6;
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
