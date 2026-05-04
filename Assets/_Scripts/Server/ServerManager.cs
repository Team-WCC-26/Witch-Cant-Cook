using System.Net.Sockets;
using System;
using UnityEngine;
using Protocol;
using Cysharp.Threading.Tasks;
using System.Threading;
using TMPro;
using MemoryPack;

namespace Server
{
    public class ServerManager : Singleton<ServerManager>
    {
        [SerializeField] TMP_InputField _input;

        public bool IsEnterRoom = false;
        private TcpClient _client = new(); // 단순 Tcp가 아니라 Socket단위로 구조 수정해서 tcp/udp 모두 받을 수 있게 해야 함
        private NetworkStream _stream;

        private PacketReceiver _packetReceiver = new();
        private PacketDispatcher _packetDispatcher = new();
        private JobWorker _jobWorker = new();

        private CancellationTokenSource _cts;

        private void Start()
        {
            Initialize(); // GameManager 등으로 넘겨야 함
        }

        public async void Initialize()
        {
            _cts = new();

            await _client.ConnectAsync("127.0.0.1", 4040);

            Debug.Log("Connected to server");

            _stream = _client.GetStream();

            _ = UniTask.RunOnThreadPool(() =>
            {
                _ = _packetReceiver.StartAsync(_stream, _cts.Token);
            });

            _jobWorker.Initialize();
            _ = _jobWorker.StartProcess(_cts.Token);
        }

        public void PushJob(Action job)
        {
            _jobWorker.Push(job);
        }

        public async UniTask SendData(byte[] data)
        {
            await _stream.WriteAsync(data);
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

        public void Input()
        {
            var input = _input.text;

            if (string.IsNullOrEmpty(input)) return;

            byte[] data;

            if (input[0] == '/')
            {
                string[] command = input.Split(" ");

                switch (command[0])
                {
                    case "/create":
                        CreateRoom();
                        break;

                    case "/enter":
                        EnterRoom(command[1]);
                        break;

                    case "/rooms":
                        GetRooms();
                        break;

                    //case "/exit":
                    //    data = new byte[1];
                    //    break;

                    default:
                        return;
                }
            }
            else if (_packetReceiver.IsEnterRoom)
            {
                ChatMessagePacket chatPacket = new()
                {
                    Message = input
                };
                data = PacketSerializer.Serialize(chatPacket);
            }
        }

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
