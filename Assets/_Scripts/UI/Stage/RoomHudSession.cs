using System;
using Protocol;
using Server;
using UnityEngine;

// Persistent data receiver only. UIManager loads the HUD prefab independently.
public sealed class RoomHudSession : MonoBehaviour
{
    private static RoomHudSession instance;
    private ServerManager server;
    private StageSelectionSession stages;
    public RoomHudState State { get; } = new();
    public LobbyHudState Lobby { get; } = new();
    public event Action Changed;
    public event Action<StageSettlementData> SettlementRecorded;

    public static RoomHudSession Instance
    {
        get
        {
            if (instance == null)
            {
                var owner = new GameObject(nameof(RoomHudSession));
                instance = owner.AddComponent<RoomHudSession>();
                DontDestroyOnLoad(owner);
            }
            instance.TryRegister();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize() => _ = Instance;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    private void OnEnable()
    {
        stages = StageSelectionSession.Instance;
        stages.Changed += OnStageChanged;
        StageManager.DoorOpened += OnDoorOpened;
        OnStageChanged();
    }

    private void Update()
    {
        if (server == null) TryRegister();
    }

    private void TryRegister()
    {
        if (server != null) return;
        server = FindFirstObjectByType<ServerManager>();
        if (server != null) server.RegisterHandler(PacketId.S_RoomMoney, OnRoomMoney);
    }

    public void OnRoomJoined()
    {
        State.ResetRoom();
        Lobby.Reset();
        OnStageChanged();
    }

    public void SetRoomInfo(string code, string localPlayerId, string otherPlayerId)
    {
        Lobby.Enter(code, localPlayerId, otherPlayerId, Time.unscaledTimeAsDouble);
        if (State.IsStageRunning) Lobby.BeginStage();
        Changed?.Invoke();
    }

    public void OnPlayerEntered(string id) { if (Lobby.AddPlayer(id)) Changed?.Invoke(); }
    public void OnPlayerLeft(string id) { if (Lobby.RemovePlayer(id)) Changed?.Invoke(); }
    // Remote voice SDK callbacks should call this with the associated protocol player ID.
    public void SetPlayerSpeaking(string id, bool speaking) { if (Lobby.SetSpeaking(id, speaking)) Changed?.Invoke(); }
    public void SetLobbyDuration(float seconds) { if (Lobby.DurationSeconds == seconds) return; Lobby.SetDuration(seconds); Changed?.Invoke(); }
    public void SetLobbyRemainingSeconds(float seconds) { Lobby.SetRemainingSeconds(seconds, Time.unscaledTimeAsDouble); Changed?.Invoke(); }

    private void OnDoorOpened(DoorId doorId)
    {
        if (doorId != DoorId.Lobby || !Lobby.IsInRoom || State.IsStageRunning) return;
        Lobby.BeginLobby(Time.unscaledTimeAsDouble);
        Changed?.Invoke();
    }

    private void OnRoomMoney(ReadOnlyMemory<byte> bytes)
    {
        int total = PacketSerializer.Deserialize<RoomMoneyPacket>(bytes).Money;
        if (State.ApplyMoney(total)) Changed?.Invoke();
    }

    private void OnStageChanged()
    {
        StageSettlementData previousSettlement = State.LatestSettlement;
        State.ApplyStage(stages.SelectedStageId, stages.HasStageSelection, stages.IsStageRunning,
            Time.unscaledTimeAsDouble);
        if (State.IsStageRunning) Lobby.BeginStage();
        Changed?.Invoke();
        if (State.LatestSettlement != null && State.LatestSettlement != previousSettlement)
            SettlementRecorded?.Invoke(State.LatestSettlement);
    }

    private void OnDisable()
    {
        if (stages != null) stages.Changed -= OnStageChanged;
        StageManager.DoorOpened -= OnDoorOpened;
        if (server != null) server.UnRegisterHandler(PacketId.S_RoomMoney);
        server = null;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
