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
    public int PendingMoneyPacketCount { get; private set; }
    public int? LastReceivedPendingMoney { get; private set; }
    public int? PendingMoneyBeforeLastPacket { get; private set; }
    public int? PendingMoneyAfterLastPacket { get; private set; }
    public double LastPendingMoneyReceivedAt { get; private set; }
    public bool LastPendingMoneyStageRunning { get; private set; }
    public event Action Changed;
    public event Action RoomJoined;
    public event Action StageTimeEnded;
    public event Action LobbyMoveReceived;
    public event Action<StageSettlementData> SettlementRecorded;
    // The server broadcasts a pending snapshot for each rewarded submission.
    public event Action<int, int> PendingMoneyIncreased;

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
        stages.StageStopped += OnStageStopped;
        stages.StageResultReceived += OnStageResult;
        stages.LobbyMoveReceived += OnLobbyMove;
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
        if (server == null) return;
        server.RegisterHandler(PacketId.S_RoomMoney, OnRoomMoney);
        server.RegisterHandler(PacketId.S_RoomPendingMoney, OnRoomPendingMoney);
    }

    public void OnRoomJoined()
    {
        State.ResetRoom();
        PendingMoneyPacketCount = 0;
        LastReceivedPendingMoney = null;
        PendingMoneyBeforeLastPacket = null;
        PendingMoneyAfterLastPacket = null;
        LastPendingMoneyReceivedAt = 0;
        LastPendingMoneyStageRunning = false;
        Lobby.Reset();
        RoomJoined?.Invoke();
        OnStageChanged();
    }

    public void NotifyLobbyReturned()
    {
        stages.NotifyLobbyReturned();
        Lobby.BeginLobby(Time.unscaledTimeAsDouble);
        Changed?.Invoke();
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
        // Door animation alone no longer authorizes the post-stage lobby transition.
        if (State.HasActiveStage || State.LatestSettlement != null) return;
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
        State.ApplyStage(stages.SelectedStageId, stages.HasStageSelection, stages.IsStageRunning,
            Time.unscaledTimeAsDouble);
        if (State.IsStageRunning) Lobby.BeginStage();
        Changed?.Invoke();
    }

    private void OnRoomPendingMoney(ReadOnlyMemory<byte> bytes)
    {
        int total = PacketSerializer.Deserialize<RoomPendingMoneyPacket>(bytes).Money;
        PendingMoneyBeforeLastPacket = State.HasPendingMoney ? State.PendingMoney : (int?)null;
        LastReceivedPendingMoney = total;
        LastPendingMoneyReceivedAt = Time.unscaledTimeAsDouble;
        LastPendingMoneyStageRunning = State.IsStageRunning;
        PendingMoneyPacketCount++;
        bool changed = State.ApplyPendingMoney(total);
        PendingMoneyAfterLastPacket = State.HasPendingMoney ? State.PendingMoney : (int?)null;
        Debug.Log($"[Money Pending] received={total}, displayed={State.PendingMoney}, stageRunning={State.IsStageRunning}, changed={changed}", this);
        if (changed) Changed?.Invoke();
        if (changed && PendingMoneyBeforeLastPacket.HasValue && total > PendingMoneyBeforeLastPacket.Value)
            PendingMoneyIncreased?.Invoke(total - PendingMoneyBeforeLastPacket.Value, total);
    }

    private void OnStageStopped(StageStopPacket packet)
    {
        if (!State.ApplyStageTimeEnded()) return;
        StageTimeEnded?.Invoke();
        Changed?.Invoke();
    }

    private void OnLobbyMove() => LobbyMoveReceived?.Invoke();

    private void OnStageResult(StageResultPacket packet)
    {
        int? goal = null;
        DataManager data = DataManager.Instance;
        if (data != null && data.IsDataLoaded)
            foreach (Stage stage in data.GetStage().GetAll())
                if (stage != null && stage.stageNum == State.StageNumber) { goal = stage.goal; break; }
        if (!State.ApplyStageSettlement(packet.PendingMoney, packet.Money, packet.IsSuccess, goal)) return;
        Changed?.Invoke();
        SettlementRecorded?.Invoke(State.LatestSettlement);
    }

    private void OnDisable()
    {
        if (stages != null)
        {
            stages.Changed -= OnStageChanged;
            stages.StageStopped -= OnStageStopped;
            stages.StageResultReceived -= OnStageResult;
            stages.LobbyMoveReceived -= OnLobbyMove;
        }
        StageManager.DoorOpened -= OnDoorOpened;
        if (server != null)
        {
            server.UnRegisterHandler(PacketId.S_RoomMoney);
            server.UnRegisterHandler(PacketId.S_RoomPendingMoney);
        }
        server = null;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
