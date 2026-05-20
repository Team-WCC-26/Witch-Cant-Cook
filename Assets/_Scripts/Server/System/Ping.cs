using Cysharp.Threading.Tasks;
using MemoryPack;
using Protocol;
using Server;
using System;
using System.Threading;
using UnityEngine;

public class Ping : MonoBehaviour
{
    private PacketId _pongId => PacketId.S_Pong;

    private float _pingDelay = 1f;
    private float _pingTimer = 0;

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(_pongId, GetPong);
    }

    private void OnDisable()
    {
        ServerManager.Instance.UnRegisterHandler(_pongId);
    }

    private void Update()
    {
        _pingTimer += Time.unscaledDeltaTime;

        if (_pingTimer >= _pingDelay)
        {
            _pingTimer = 0f;

            SendPing();
        }
    }

    private void GetPong(ReadOnlyMemory<byte> data)
    {
        var pongPacket = MemoryPackSerializer.Deserialize<PingPongPacket>(data.Span);
        var ping = TimeUtil.NowMs() - pongPacket.TimeMs; // 외부 UI로 연결해주면 될듯

        PingUI.Instance?.UpdatePing(ping);

        PingResultPacket packet = new()
        {
            Ping = ping
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void SendPing()
    {
        PingPongPacket packet = new()
        {

            TimeMs = TimeUtil.NowMs()
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }
}
