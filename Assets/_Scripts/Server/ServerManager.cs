using System.Net.Sockets;
using System;
using UnityEngine;
using Protocol;
using Cysharp.Threading.Tasks;
using System.Threading;

// 직접 씬에 배치하지 말 것.
namespace Server
{
    public class ServerManager : Singleton<ServerManager>
    {
        private string _hostIP = "142.249.56.163";
        private bool _useLocalHost = false;

        // 재접속 시퀀스 on/off 토글 - 코드에서 직접 변경한다.
        // true = 재접속 시퀀스 전체(Hello 전송/티켓 저장/끊김 감지 복구/재접속 시도/S_Heartbeat 에코) 활성.
        // false = 완전 레거시 동작(Hello 미전송, 워치독·keepalive 없음, 끊겨도 아무 복구 없음 - 기존 클라와 100% 동일).
        // 실행 중 변경 시 다음 판정 시점부터 적용된다.
        public bool UseReconnectSequence = false;

        public WorldStateRouter Router { get; } = new();
        public bool IsEnterRoom = false;

        private string _localHost = "127.0.0.1";

        private TcpClient _client = new(); // 단순 Tcp가 아니라 Socket단위로 구조 수정해서 tcp/udp 모두 받을 수 있게 해야 함
        private NetworkStream _stream;
        private bool _sendFailedLogged = false; // 현재 연결에서 송신 실패 로그를 남겼는지 (스팸 방지) - 새 연결마다 OpenFreshConnectionAsync에서 리셋

        private PacketReceiver _packetReceiver = new();
        private PacketDispatcher _packetDispatcher = new();
        private JobWorker _jobWorker = new();
        private SessionReconnector _reconnector = new(); // 연결 재활용 기반 (끊김 감지/재접속/신규 접속 플로우) - ServerManager가 소유

        private CancellationTokenSource _cts;
        private CancellationTokenSource _receiveCts; // 수신 전용 CTS - 재접속 정리 시 이것만 취소한다 (공용 _cts를 취소하면 JobWorker의 StartProcess 루프 토큰도 함께 취소되므로 분리)

        private void Start()
        {
            Initialize(); // GameManager 등으로 넘겨야 함
        }

        public async void Initialize()
        {
            _cts = new();
            //_packetDispatcher.Initialize();
            Router.Initialize();
            _jobWorker.Initialize();
            _reconnector.Initialize(); // 시스템 핸들러(S_SessionTicket/S_ReconnectResult/S_Heartbeat/S_ReconnectSnapshot) 등록 + 수신 정지 워치독 시작

            _ = _jobWorker.StartProcess(_cts.Token);

            await StartSessionAsync();
        }

        /// <summary>
        /// 신규 접속 플로우 - 첫 접속(Initialize 경로)과 재접속 실패 후(SessionReconnector)가
        /// 동일 코드 경로를 타도록 기존 접속 절차를 공용화했다.
        /// connect → 스트림 수립 → 세션 Hello 전송 → 접속별 세션 상태 리셋.
        /// </summary>
        public async UniTask StartSessionAsync()
        {
            await OpenFreshConnectionAsync();

            // 접속 수립 직후 세션 협상 - C_SessionHello 전송.
            // 재접속 스위치가 꺼진 서버에서는 이 패킷이 유지보수용 게이팅 패킷이라 안전하게 무시되고,
            // 첫 일반 패킷이 오면 그때 라이브(Live) 전환되어 기존 게임플레이 흐름을 그대로 진행한다
            // 재접속 시퀀스 토글(UseReconnectSequence)이 ON일 때만 Hello를 전송한다 - OFF면 완전 레거시 동작으로 미전송
            if (UseReconnectSequence)
            {
                SessionHelloPacket helloPacket = new()
                {
                    ProtocolVersion = 1,
                    Capabilities = (int)(SessionCapability.Reconnect | SessionCapability.ServerHeartbeat),
                    RequestId = TimeUtil.NowMs(), // Unity 참조 어셈블리(.NET Standard 2.1)에는 Environment.TickCount64가 없어 Stopwatch 기반 TimeUtil.NowMs()로 대체
                };
                await SendData(PacketSerializer.Serialize(helloPacket));
            }

            // 새 세션의 접속별 상태 리셋 - 티켓 대기 타이머와 끊김 콜백 멱등 가드가 여기서 새로 준비된다
            _reconnector.BeginSession(_packetReceiver);
        }

        /// <summary>
        /// 공용 접속 절차 - 기존 연결을 완전히 정리한 뒤 TcpClient/스트림을 다시 수립하고 수신 태스크를 스타트한다.
        /// 닫혀버린 소켓은 재사용할 수 없으므로 재접속마다 TcpClient를 새로 만든다.
        /// Hello 없이 재접속 요청만 보내는 시도 경로와, Hello를 이어서 보내는 신규 접속 경로 양쪽이 이 메서드를 공유한다
        /// </summary>
        public async UniTask OpenFreshConnectionAsync()
        {
            TeardownConnection();
            _sendFailedLogged = false; // 연결별 송신 실패 로그 리셋

            string host = _useLocalHost ? _localHost : _hostIP;

            _receiveCts = new(); // 이 연결에만 귀속되는 수신 전용 토큰
            _client = new TcpClient();
            await _client.ConnectAsync(host, 4040);

            Debug.Log("Connected to server");

            _stream = _client.GetStream();

            _packetReceiver = new PacketReceiver();

            // 스레드풀 람다가 필드(_packetReceiver/_stream/_receiveCts)를 실행 중에 재읽지 않게
            // 시작 시점의 값을 로컬로 캡처해 전달한다 - 두 접속 절차가 겹쳐도 수신 태스크는
            // 자기 세대의 수신 인스턴스/스트림/토큰에만 묶인다
            PacketReceiver receiver = _packetReceiver;
            NetworkStream stream = _stream;
            CancellationTokenSource receiveCts = _receiveCts;

            _ = UniTask.RunOnThreadPool(() =>
            {
                _ = receiver.StartAsync(stream, receiveCts.Token, _reconnector.NotifyConnectionLost);
            });
        }

        /// <summary>
        /// 현재 연결 정리 - 수신 태스크에 정리 표시를 선(先)으로 하고(Unity Mono BCL의 ReadAsync는 대기 중 취소가
        /// 안 걸려 close 시 IOException/ObjectDisposedException으로 빠지므로, 표시된 예외는 끊김 콜백 없이 소멸한다)
        /// 수신 전용 CTS를 취소/Dispose하고 스트림/소켓을 닫는다. _stream은 null로 두어 정리된 연결로의 잔여 쓰기가
        /// 죽은 스트림을 향하지 않게 한다. JobWorker의 _cts는 건드리지 않으므로 재접속 중에도 작업 큐는 계속 동작한다
        /// </summary>
        public void TeardownConnection()
        {
            // 1) 수신 태스크가 의도 정리임을 제일 먼저 알게 한다 - 이후 발생하는 예외는 끊김 콜백을 발화하지 않는다
            PacketReceiver receiver = _packetReceiver;

            if (receiver != null)
            {
                receiver.IsClosing = true;
            }

            // 2) 수신 전용 CTS 취소/Dispose - 이 CTS에 귀속된 수신 루프만 종료된다 (JobWorker의 _cts 불변)
            CancellationTokenSource receiveCts = _receiveCts;
            _receiveCts = null;

            try
            {
                receiveCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 이미 Dispose된 CTS - 무시한다
            }

            receiveCts?.Dispose();

            // 3) 스트림/소켓 닫기 - _stream은 먼저 null로 비워 정리된 연결로의 잔여 쓰기를 막는다
            NetworkStream stream = _stream;
            _stream = null;
            TcpClient client = _client;

            try
            {
                stream?.Close();
            }
            catch (Exception)
            {
                // 이미 닫힌 스트림 등 - 무시한다
            }

            try
            {
                client?.Close();
            }
            catch (Exception)
            {
                // 이미 닫힌 소켓 등 - 무시한다
            }
        }

        /// <summary>
        /// 아직 열려 있는 스트림에 C_Disconnect 원시 프레임(6바이트 헤더만: 길이 0 + C_Disconnect ID, 빈 본문)을
        /// 최선을 다해 전송한다. 서버는 명시적 종료를 받으면 토큰을 즉시 무효화하고 방 정리를 진행한다.
        /// 죽은 스트림이면 실패는 무시한다 (재접속 기한 만료/거절 후 세션 정리 용도)
        /// </summary>
        public void SendDisconnectFrameIfOpen()
        {
            TcpClient client = _client;
            NetworkStream stream = _stream;

            // 정리된(teardown된) 연결에는 쓰지 않는다 - 스트림이 비었거나 소켓이 닫혔으면 건너뛴다
            if (stream == null || client == null || !client.Connected) return;

            byte[] frame = new byte[6]; // length(int, 4바이트) + id(ushort, 2바이트) - 빈 본문
            BitConverter.GetBytes(0).CopyTo(frame, 0);
            BitConverter.GetBytes((ushort)PacketId.C_Disconnect).CopyTo(frame, 4);

            try
            {
                stream.Write(frame, 0, frame.Length);
            }
            catch (Exception ex)
            {
                // 이미 닫힌 스트림이라면 실패를 무시한다 (정리는 닫기 처리에서 완료된다)
                Debug.LogWarning($"[ServerManager] C_Disconnect 프레임 전송 실패(무시) - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public void PushJob(Action job)
        {
            _jobWorker.Push(job);
        }

        public async UniTask SendData(byte[] data)
        {
            if (_stream == null)
            {
                // 정리된 연결(재접속 창 등) - 게임플레이 송신이 되풀이되므로 회당 오류 로그를 연결당 1회만 남긴다
                if (!_sendFailedLogged)
                {
                    _sendFailedLogged = true;
                    Debug.LogError($"STREAM = {_stream}.");
                }
                return;
            }
            try
            {
                await _stream.WriteAsync(data);
            }
            catch (Exception ex)
            {
                // 끊긴 스트림으로의 송신은 재접속 창에서 일시적으로 반복될 수 있다 - 연결당 1회만 로그를 남기고 조용히 실패 처리
                // (끊김 판정/회복은 수신 루프와 SessionReconnector가 담당하므로 여기서 예외를 배출하지 않는다)
                if (!_sendFailedLogged)
                {
                    _sendFailedLogged = true;
                    Debug.LogWarning($"[ServerManager] 송신 실패 (연결 끊김 추정) - {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        #region Packet Dispatcher

        public void RegisterHandler(PacketId id, Action<ReadOnlyMemory<byte>> action)
        {
            _packetDispatcher.Register(id, action);
        }

        public void UnRegisterHandler(PacketId id)
        {
            _packetDispatcher.UnRegister(id);
        }

        public void DispatchPacket(PacketId id, ReadOnlyMemory<byte> data)
        {
            _packetDispatcher.Dispatch(id, data);
        }

        #endregion

        public void CreateRoom()
        {
            CreateRoomPacket createRoomPacket = new();
            var data = PacketSerializer.Serialize(createRoomPacket);
            SendData(data);
        }

        public void EnterRoom(string id)
        {
            if (IsEnterRoom) return;

            JoinRoomPacket joinRoomPacket = new()
            {
                RoomId = id
            };
            var data = PacketSerializer.Serialize(joinRoomPacket);
            SendData(data);
        }

        public void GetRooms()
        {
            GetRoomPacket getRoomPacket = new();
            var data = PacketSerializer.Serialize(getRoomPacket);
            SendData(data);
        }

        public void ExitRoom()
        {

        }
    }
}
