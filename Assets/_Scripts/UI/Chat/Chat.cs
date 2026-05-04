using MemoryPack;
using Protocol;
using Server;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Chat : MonoBehaviour
{
    [SerializeField] private TMP_Text _chatLog;
    [SerializeField] private TMP_InputField _input;
    [SerializeField] private Button _sendButton;

    private PacketId _chatID => PacketId.S_ChatMessage;
    private PacketId _notificationID => PacketId.S_Notification;

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(_chatID, GetChat);
        ServerManager.Instance.RegisterHandler(_notificationID, GetNotification);
        _sendButton.onClick.AddListener(SendText);
    }

    private void OnDisable()
    {
        ServerManager.Instance.UnRegisterHandler(_chatID);
        ServerManager.Instance.UnRegisterHandler(_notificationID);
        _sendButton.onClick.RemoveListener(SendText);
    }

    private void GetNotification(ReadOnlyMemory<byte> data)
    {
        var packet = MemoryPackSerializer.Deserialize<RoomNotificationPacket>(data.Span);

        AddText($"\n{packet.Message}");
    }

    private void GetChat(ReadOnlyMemory<byte> data)
    {
        var packet = MemoryPackSerializer.Deserialize<ChatMessagePacket>(data.Span);

        AddText($"\n{packet.Sender} : {packet.Message}");
    }

    private void SendText()
    {
        ChatMessagePacket packet = new()
        {
            Message = _input.text
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void AddText(string text)
    {
        _chatLog.text += text;
    }
}
