using System;
using Protocol;
using Server;
using UnityEngine;

// Server-owned stage state. Boards and the HUD share this receiver; clients never select a stage here.
public sealed class StageSelectionSession : MonoBehaviour
{
    private static StageSelectionSession instance;
    private ServerManager server;
    private PlayerSpawnManager players;
    private bool stageEndPending;
    private bool hasLeftRoom;

    public static StageSelectionSession Instance
    {
        get
        {
            if (instance == null)
            {
                var owner = new GameObject(nameof(StageSelectionSession));
                instance = owner.AddComponent<StageSelectionSession>();
                DontDestroyOnLoad(owner);
            }
            instance.TryRegister();
            return instance;
        }
    }

    public int SelectedStageId { get; private set; }
    public bool HasStageSelection { get; private set; }
    public bool IsStageRunning { get; private set; }
    public bool IsRoomReady => server != null && server.IsConnected && players != null &&
        !string.IsNullOrEmpty(players.MyID) && players.ContainsPlayer(players.MyID);

    public event Action Changed;
    public event Action<int> StageSelected;
    public event Action<int> StageStarted;
    public event Action<StageStopPacket> StageStopped;
    public event Action<StageResultPacket> StageResultReceived;
    public event Action LobbyMoveReceived;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize() => _ = Instance;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    private void OnEnable() => DoorController.DoorOpened += OnDoorOpened;

    private void Update()
    {
        if (server == null) TryRegister();
        if (players == null) players = FindFirstObjectByType<PlayerSpawnManager>();
    }

    private void TryRegister()
    {
        if (server != null) return;
        server = FindFirstObjectByType<ServerManager>();
        if (server == null) return;
        server.RegisterHandler(PacketId.S_StageSet, OnStageSet);
        server.RegisterHandler(PacketId.S_StageStart, OnStageStart);
        server.RegisterHandler(PacketId.S_StageStop, OnStageStop);
        server.RegisterHandler(PacketId.S_StageResult, OnStageResult);
        server.RegisterHandler(PacketId.S_LobbyMove, OnLobbyMove);
        players = FindFirstObjectByType<PlayerSpawnManager>();
    }

    // Called by the existing lobby UI, which already owns S_JoinRoom.
    // Door state comes from StageManager.DoorOpened, so neither handler is replaced.
    public void OnRoomJoined()
    {
        // Creators and joining players both display stage 1 initially.
        // A subsequent server stage packet replaces this initial display.
        hasLeftRoom = false;
        SelectedStageId = 1;
        IsStageRunning = false;
        HasStageSelection = true;
        stageEndPending = false;
        RoomHudSession.Instance.OnRoomJoined();
        Changed?.Invoke();
    }

    public void OnRoomLeft()
    {
        hasLeftRoom = true;
        SelectedStageId = 0;
        IsStageRunning = false;
        HasStageSelection = false;
        stageEndPending = false;
        RoomHudSession.Instance.OnRoomLeft();
        Changed?.Invoke();
    }

    private void OnStageSet(ReadOnlyMemory<byte> bytes)
    {
        if (hasLeftRoom) return;
        int stageId = PacketSerializer.Deserialize<StageSetPacket>(bytes).StageNum;
        if (stageId <= 0) return;
        SelectedStageId = stageId;
        HasStageSelection = true;
        Changed?.Invoke();
        StageSelected?.Invoke(SelectedStageId);
    }

    private void OnStageStart(ReadOnlyMemory<byte> bytes)
    {
        if (hasLeftRoom) return;
        if (stageEndPending) return;
        int stageId = PacketSerializer.Deserialize<StageStartPacket>(bytes).StageNum;
        if (stageId <= 0) return;
        SelectedStageId = stageId;
        HasStageSelection = true;
        IsStageRunning = true;
        Changed?.Invoke();
        StageStarted?.Invoke(SelectedStageId);
    }

    private void OnStageStop(ReadOnlyMemory<byte> bytes)
    {
        if (hasLeftRoom) return;
        StageStopPacket packet = PacketSerializer.Deserialize<StageStopPacket>(bytes);
        if (stageEndPending || !IsStageRunning) return;
        stageEndPending = true;
        IsStageRunning = false;
        // Time ended only. Pending rewards may still arrive before S_StageResult.
        StageStopped?.Invoke(packet);
        Changed?.Invoke();
    }

    private void OnStageResult(ReadOnlyMemory<byte> bytes)
    {
        if (hasLeftRoom) return;
        StageResultPacket packet = PacketSerializer.Deserialize<StageResultPacket>(bytes);
        if (!stageEndPending && !IsStageRunning) return;
        stageEndPending = true;
        IsStageRunning = false;
        StageResultReceived?.Invoke(packet);
        Changed?.Invoke();
    }

    private void OnLobbyMove(ReadOnlyMemory<byte> bytes)
    {
        if (hasLeftRoom) return;
        PacketSerializer.Deserialize<LobbyMovePacket>(bytes);
        if (stageEndPending) LobbyMoveReceived?.Invoke();
    }

    public void NotifyLobbyReturned()
    {
        stageEndPending = false;
        IsStageRunning = false;
    }

    private void OnDoorOpened(DoorId doorId)
    {
        if (hasLeftRoom) return;
        if (doorId != DoorId.Kitchen || stageEndPending) return;
        IsStageRunning = true;
        Changed?.Invoke();
    }

    private void OnDisable()
    {
        DoorController.DoorOpened -= OnDoorOpened;
        if (server != null)
        {
            server.UnRegisterHandler(PacketId.S_StageSet);
            server.UnRegisterHandler(PacketId.S_StageStart);
            server.UnRegisterHandler(PacketId.S_StageStop);
            server.UnRegisterHandler(PacketId.S_StageResult);
            server.UnRegisterHandler(PacketId.S_LobbyMove);
        }
        server = null;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
