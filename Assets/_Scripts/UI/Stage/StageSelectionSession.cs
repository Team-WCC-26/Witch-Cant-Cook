using System;
using Cysharp.Threading.Tasks;
using Protocol;
using Server;
using UnityEngine;

// UI-owned stage packet receiver. Uses the existing ServerManager API unchanged.
// Boards and the HUD share this receiver instead of registering competing handlers.
public sealed class StageSelectionSession : MonoBehaviour
{
    private static StageSelectionSession instance;
    private ServerManager server;
    private PlayerSpawnManager players;
    private bool selectionSyncNeeded;
    private int pendingRequestedStage;
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

    public int SelectedStageId { get; private set; } = 1;
    public bool HasStageSelection { get; private set; }
    public bool IsStageRunning { get; private set; }
    public bool IsRoomReady => server != null && players != null &&
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
        SynchronizeNewPlayer();
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
    public void OnRoomJoined(bool hasOtherPlayers = false)
    {
        hasLeftRoom = false;
        SelectedStageId = 1;
        IsStageRunning = false;
        HasStageSelection = !hasOtherPlayers;
        selectionSyncNeeded = false;
        pendingRequestedStage = 0;
        stageEndPending = false;
        RoomHudSession.Instance.OnRoomJoined();
        Changed?.Invoke();
    }

    public void OnRoomLeft()
    {
        hasLeftRoom = true;
        SelectedStageId = 1;
        IsStageRunning = false;
        HasStageSelection = false;
        selectionSyncNeeded = false;
        pendingRequestedStage = 0;
        stageEndPending = false;
        RoomHudSession.Instance.OnRoomLeft();
        Changed?.Invoke();
    }

    // Called from PlayerSpawnManager's existing S_PlayerEnter handler, only on existing members.
    public void OnPlayerEntered(string playerId)
    {
        if (players == null) players = FindFirstObjectByType<PlayerSpawnManager>();
        if (string.IsNullOrEmpty(playerId) || players == null || playerId == players.MyID) return;
        selectionSyncNeeded = true;
    }

    private async void SynchronizeNewPlayer()
    {
        if (!selectionSyncNeeded || !IsRoomReady || !HasStageSelection || IsStageRunning ||
            pendingRequestedStage != 0) return;

        // Reuse the server's existing broadcast. Wait for our own page-change acknowledgment
        // first so an older cached page cannot overwrite a selection made during the join.
        selectionSyncNeeded = false;
        try
        {
            await server.SendData(PacketSerializer.Serialize(new StageSetPacket { StageNum = SelectedStageId }));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    public async UniTask RequestSelection(int stageId)
    {
        TryRegister();
        if (!IsRoomReady) throw new InvalidOperationException("Join a room before selecting a stage.");
        if (StageReturnFlow.IsInputBlocked) throw new InvalidOperationException("Wait for the lobby return before selecting a stage.");
        if (!HasStageSelection) throw new InvalidOperationException("Wait for the room's selected stage before changing pages.");
        pendingRequestedStage = stageId;
        try
        {
            await server.SendData(PacketSerializer.Serialize(new StageSetPacket { StageNum = stageId }));
        }
        catch
        {
            if (pendingRequestedStage == stageId) pendingRequestedStage = 0;
            throw;
        }
    }

    private void OnStageSet(ReadOnlyMemory<byte> bytes)
    {
        if (hasLeftRoom) return;
        SelectedStageId = PacketSerializer.Deserialize<StageSetPacket>(bytes).StageNum;
        HasStageSelection = true;
        if (pendingRequestedStage == SelectedStageId) pendingRequestedStage = 0;
        Changed?.Invoke();
        StageSelected?.Invoke(SelectedStageId);
    }

    private void OnStageStart(ReadOnlyMemory<byte> bytes)
    {
        if (hasLeftRoom) return;
        if (stageEndPending) return;
        SelectedStageId = PacketSerializer.Deserialize<StageStartPacket>(bytes).StageNum;
        HasStageSelection = true;
        pendingRequestedStage = 0;
        selectionSyncNeeded = false;
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
        selectionSyncNeeded = false;
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
