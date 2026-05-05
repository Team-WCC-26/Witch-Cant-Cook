using MemoryPack;
using System;
using System.Collections.Generic;
using UI.Slot;
using Protocol;
using Server;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LobbyRouterUI : SlotHandlerUIBase<LobbySlotData, LobbySlot>
{
    [SerializeField] private Sprite _privateSprite;
    [SerializeField] private float _refreshDelay = 5f;

    [Header("External UIs")]
    [SerializeField] private Chat _chat;

    [Header("Sub UIs")]
    [SerializeField] private Button _refreshButton;
    [SerializeField] private PasswordInput _passwordInput;

    private PacketId _getRoomID => PacketId.S_GetRoom;
    private PacketId _joinRoomID => PacketId.S_JoinRoom;
    private float _lastRefreshTime = 0;

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(_getRoomID, InitLobby);
        ServerManager.Instance.RegisterHandler(_joinRoomID, JoinRoom);
        _refreshButton.onClick.AddListener(RefreshLobby);
        //_passwordInput.OnButtonClicked += SendJoinRoomPacket;
    }

    private void OnDisable()
    {
        ServerManager.Instance.UnRegisterHandler(_getRoomID);
        ServerManager.Instance.UnRegisterHandler(_joinRoomID);
        _refreshButton.onClick.RemoveListener(RefreshLobby);
        //_passwordInput.OnButtonClicked -= SendJoinRoomPacket;
    }

    public void RefreshLobby()
    {
        var curTime = Time.realtimeSinceStartup;

        if (curTime - _lastRefreshTime < _refreshDelay) return;

        _lastRefreshTime = curTime;

        GetRoomPacket packet = new();
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void InitLobby(ReadOnlyMemory<byte> data)
    {
        var packet = MemoryPackSerializer.Deserialize<GetRoomPacket>(data.Span);
        List<LobbySlotData> slotDatas = new();

        foreach (var roomData in packet.RoomDatas)
        {
            LobbySlotData slotData = new()
            {
                RoomData = roomData,
                Sprite = _privateSprite
            };

            slotDatas.Add(slotData);
        }

        SetData(slotDatas);
    }

    private void SendJoinRoomPacket(string id, string password = "")
    {
        JoinRoomPacket packet = new()
        {
            RoomId = id,
            RoomPassword = password
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void JoinRoom(ReadOnlyMemory<byte> data)
    {
        //_chat.gameObject.SetActive(true);

        PlayerSpawnManager.Instance.MyID = "0";

        SpawnPlayerByIndex(0);

        //방에 2명 이상일 때만 이거 실행하도록 하기.
        //SpawnPlayerByIndex(1);

        UIManager.Hide<LobbyRouterUI>();
    }

    private void SpawnPlayerByIndex(int index)
    {
        string playerId = index.ToString();

        if (PlayerSpawnManager.Instance.ContainsPlayer(playerId)) return;

        PlayerSpawnManager.Instance.SpawnPlayer(playerId);
    }

    #region Pointer Event

    public override void OnDoubleClick()
    {
        int index = GetPointedSlotIndex();
        var roomData = _dataContainer.Datas[index];

        if (roomData.RoomData.BIsPrivate)
        {

        }
        else
        {
            SendJoinRoomPacket(roomData.RoomData.Id);
        }
    }

    public override void OnLeftClick()
    {
    }

    public override void OnRightClick()
    {
    }

    public override void OnPointerIn()
    {
    }

    public override void OnPointerOut()
    {
    }

    #endregion
}
