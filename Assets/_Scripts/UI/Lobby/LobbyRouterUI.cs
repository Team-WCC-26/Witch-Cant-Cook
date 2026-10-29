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
    [SerializeField] private GameObject _emptyState;
    private ServerManager registeredServer;

    private PacketId _getRoomID => PacketId.S_GetRoom;
    private PacketId _joinRoomID => PacketId.S_JoinRoom;
    private float _lastRefreshTime = float.NegativeInfinity;

    private void OnEnable()
    {
        registeredServer = ServerManager.Instance;
        if (registeredServer != null)
        {
            registeredServer.RegisterHandler(_getRoomID, InitLobby);
            registeredServer.RegisterHandler(_joinRoomID, JoinRoom);
        }
        _refreshButton.onClick.AddListener(RefreshLobby);
        if (_passwordInput != null) _passwordInput.OnButtonClicked += SendJoinRoomPacket;
    }

    private void OnDisable()
    {
        if (registeredServer != null)
        {
            registeredServer.UnRegisterHandler(_getRoomID);
            registeredServer.UnRegisterHandler(_joinRoomID);
        }
        registeredServer = null;
        _refreshButton.onClick.RemoveListener(RefreshLobby);
        if (_passwordInput != null) _passwordInput.OnButtonClicked -= SendJoinRoomPacket;
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

    public override void SetData(IEnumerable<LobbySlotData> datas)
    {
        var rooms = new List<LobbySlotData>(datas);
        while (_slots.Count > rooms.Count)
        {
            int last = _slots.Count - 1;
            LobbySlot slot = _slots[last];
            _slots.RemoveAt(last);
            slot.gameObject.SetActive(false);
            Destroy(slot.gameObject);
        }
        while (_slots.Count < rooms.Count) CreateSlot();
        for (int i = 0; i < rooms.Count; i++) _slots[i].Init(rooms[i]);
        _dataContainer.SetData(rooms);
        _pointedSlot = null;
        if (_emptyState != null) _emptyState.SetActive(rooms.Count == 0);
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
        var packet = MemoryPackSerializer.Deserialize<JoinRoomPacket>(data.Span);

        if (packet.RoomId == "N" || packet.RoomId == "F") return;
        StageSelectionSession.Instance.OnRoomJoined(packet.PlayerCnt >= 2);

        //_chat.gameObject.SetActive(true);

        PlayerSpawnManager.Instance.MyID = packet.PlayerId;

        SpawnPlayer(packet.PlayerId);

        //방에 2명 이상일 때만 이거 실행하도록 하기.
        if (packet.PlayerCnt >= 2)
        {
            SpawnPlayer(packet.OtherPlayerId);
        }

        UIManager.Hide<LobbyRouterUI>();
        RoomHudSession.Instance.SetRoomInfo(packet.RoomId, packet.PlayerId, packet.OtherPlayerId);
    }

    private void SpawnPlayer(string playerId)
    {
        if (PlayerSpawnManager.Instance.ContainsPlayer(playerId)) return;

        PlayerSpawnManager.Instance.SpawnPlayer(playerId);
    }

    #region Pointer Event

    public override void OnDoubleClick()
    {
        int index = GetPointedSlotIndex();
        if (index < 0 || index >= _dataContainer.Datas.Count) return;
        var roomData = _dataContainer.Datas[index];

        if (roomData.RoomData.BIsPrivate)
        {
            if (_passwordInput != null)
                _passwordInput.InitInfo(roomData.Name, roomData.RoomData.Id);
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
