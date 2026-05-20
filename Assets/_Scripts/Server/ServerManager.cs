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
        [SerializeField] private string _hostIP = "villainouskirby.kro.kr";
        [SerializeField] private bool _useLocalHost = false;

        public WorldStateRouter Router { get; } = new();
        public bool IsEnterRoom = false;

        private string _localHost = "127.0.0.1";

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

            string host = _useLocalHost ? _localHost : _hostIP;

            await _client.ConnectAsync(host, 4040);

            Debug.Log("Connected to server");

            _stream = _client.GetStream();

            _ = UniTask.RunOnThreadPool(() =>
            {
                _ = _packetReceiver.StartAsync(_stream, _cts.Token);
            });

            _packetDispatcher.Initialize();
            Router.Initialize();
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
