using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(LobbyRouterUI))]
public class LobbyRouter : MonoBehaviour
{
    [SerializeField] private GameObject _slotPrefab; // TODO => Pooling system으로 넘기긴 해야할듯
    [SerializeField] private Sprite _privateIcon;

    [SerializeField] private Transform _scrollInner;
    [SerializeField] private Button _refreshButton;

    [SerializeField] private float _refreshDelay = 5f;

    private List<LobbySlot> _lobbySlots = new(); // 역순으로 보여줌
    private Dictionary<string, LobbySlot> _lobbyDict = new();

    private LobbyRouterUI _lobbyRouterUI;

    private float _lastRefreshTime;

    private PacketId _packetId => PacketId.S_GetRoom;

    private void Awake()
    {
        _lobbyRouterUI = GetComponent<LobbyRouterUI>();
    }

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(_packetId, InitLobbyList);

        RefreshLobby();
    }

    private void OnDisable()
    {
        ServerManager.Instance.UnRegisterHandler(_packetId);
    }

    public void RefreshLobby()
    {
        _lastRefreshTime = Time.realtimeSinceStartup;

        GetRoomPacket packet = new();
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void InitLobbyList(ReadOnlyMemory<byte> data)
    {
        var packet = MemoryPackSerializer.Deserialize<GetRoomPacket>(data.Span);
        
        foreach (var roomData in packet.RoomDatas)
        {
            CreateLobby(roomData);
        }
    }

    public void CreateLobby(RoomData roomData)
    {
        var newSlot = Instantiate(_slotPrefab, _scrollInner).GetComponent<LobbySlot>();

        newSlot.gameObject.transform.SetAsFirstSibling();
        newSlot.Init(roomData, _privateIcon);
        _lobbySlots.Add(newSlot);

    }

    public void DisposeLobby(int index)
    {

    }

    private LobbySlot GetSlot(int index)
    {
        return _lobbySlots[_lobbySlots.Count - index];
    }
}
