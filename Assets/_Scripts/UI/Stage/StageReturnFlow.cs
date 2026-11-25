using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Protocol;
using Server;
using UnityEngine;
using Cysharp.Threading.Tasks;

// S_StageStop ends play; S_StageResult shows results. Confirmation waits for S_LobbyMove.
public sealed class StageReturnFlow : MonoBehaviour
{
    private static StageReturnFlow instance;
    public static StageReturnFlow Instance
    {
        get
        {
            if (instance == null)
            {
                var owner = new GameObject(nameof(StageReturnFlow));
                instance = owner.AddComponent<StageReturnFlow>();
                DontDestroyOnLoad(owner);
            }
            return instance;
        }
    }
    public static bool IsInputBlocked => FeatureRevealPresentation.IsPlaying ||
        (instance != null && (instance.State.BlocksInput ||
        (instance.room != null && instance.room.State.HasStageTimeExpired(Time.unscaledTimeAsDouble))));
    public static bool IsSettlementActive => instance != null && instance.State.ShowsSettlement;
    public StageReturnState State { get; } = new();
    public string ConfirmationIssue { get; private set; }
    public bool IsConfirming { get; private set; }
    public bool CanConfirmResult => !IsConfirming && !lobbyMovePending &&
        State.Phase == StageReturnPhase.Settlement;
    private const string ConfirmationPrefix = "WCC_STAGE_CONFIRM_V1:";
    private readonly HashSet<string> confirmedPlayers = new();
    private readonly Dictionary<int, HashSet<string>> shotConfirmations = new();
    private FeatureRevealPresentation selectedPresentation;
    public bool HasResultConfirmation(string playerId) => !string.IsNullOrEmpty(playerId) && confirmedPlayers.Contains(playerId);

    // Future server confirmation notifications can call this with an actual participant ID.
    public void ApplyResultConfirmation(string playerId)
    {
        if (!State.IsInSettlementFlow || string.IsNullOrEmpty(playerId) || !room.Lobby.Players.Contains(playerId)) return;
        if (confirmedPlayers.Add(playerId)) Changed?.Invoke();
    }
    public event Action Changed;
    public event Action LobbyReturnCompleted;
    private RoomHudSession room;
    private Coroutine returning;
    private Coroutine featureReveal;
    private bool wasRunning;
    private bool lobbyMovePending;
    private string lastSpawnIssue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize() => _ = Instance;
    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }
    private void OnEnable()
    {
        room = RoomHudSession.Instance;
        room.SettlementRecorded += OnSettlement;
        room.Changed += OnRoomChanged;
        room.RoomJoined += ResetRoom;
        room.RoomLeft += ResetRoom;
        room.StageTimeEnded += OnTimeEnded;
        room.LobbyMoveReceived += OnLobbyMove;
        OnRoomChanged();
    }
    private void OnDisable()
    {
        StopFeatureReveal();
        if (room != null)
        {
            room.SettlementRecorded -= OnSettlement;
            room.Changed -= OnRoomChanged;
            room.RoomJoined -= ResetRoom;
            room.RoomLeft -= ResetRoom;
            room.StageTimeEnded -= OnTimeEnded;
            room.LobbyMoveReceived -= OnLobbyMove;
        }
        if (returning != null) StopCoroutine(returning);
        returning = null;
        lobbyMovePending = false;
        State.Reset();
    }
    private void OnDestroy() { if (instance == this) instance = null; }
    private void ResetRoom()
    {
        StopFeatureReveal();
        FeatureRevealPresentation.CancelActive();
        if (returning != null) StopCoroutine(returning);
        returning = null;
        wasRunning = false;
        confirmedPlayers.Clear();
        shotConfirmations.Clear();
        selectedPresentation = null;
        lobbyMovePending = false;
        ConfirmationIssue = null;
        IsConfirming = false;
        lastSpawnIssue = null;
        State.Reset();
        PauseOrderPresentation(false);
        Changed?.Invoke();
    }
    private void OnRoomChanged()
    {
        bool running = room.State.IsStageRunning;
        if (running && !wasRunning)
        {
            StopFeatureReveal();
            // A genuine server start remains authoritative, including test start paths.
            if (returning != null) StopCoroutine(returning);
            returning = null;
            lobbyMovePending = false;
            ConfirmationIssue = null;
            IsConfirming = false;
            State.StartStage();
            confirmedPlayers.Clear();
            shotConfirmations.Clear();
            selectedPresentation = null;
            PauseOrderPresentation(false);
            Changed?.Invoke();
        }
        wasRunning = running;
        if (State.Phase == StageReturnPhase.FeatureReveal) AdvanceConfirmedShots();
    }
    private void OnSettlement(StageSettlementData data)
    {
        if (!State.RecordSettlement(data)) return;
        StopFeatureReveal();
        FeatureRevealPresentation.CancelActive();
        confirmedPlayers.Clear();
        shotConfirmations.Clear();
        selectedPresentation = data.IsSuccess
            ? FindObjectsByType<FeatureRevealPresentation>(FindObjectsSortMode.None)
                .FirstOrDefault(presentation => presentation.isActiveAndEnabled && presentation.HasPresentation(data.StageNumber))
            : null;
        SubmitTriggerZone.StopUnsentRequests();
        ConfirmationIssue = null;
        IsConfirming = false;
        Debug.Log("[Stage Return] S_StageResult 수신. 결산 확인 버튼을 기다립니다.", this);
        PauseOrderPresentation(true);
        // Final totals are authoritative. Plate return animations may finish independently.
        StopPlayerActions();
        Changed?.Invoke();
        StageSettlementWorldUI.EnsureLoadedBoards().Forget();
    }
    internal static void StopPlayerActions()
    {
        foreach (PlayerBrain player in FindObjectsByType<PlayerBrain>(FindObjectsSortMode.None))
        {
            player.Rocket?.StopFlight();
            player.ActionController?.CancelAction();
            if (player.Rb != null && !player.Rb.isKinematic)
            {
                player.Rb.linearVelocity = Vector3.zero;
                player.Rb.angularVelocity = Vector3.zero;
            }
        }
    }
    private void OnTimeEnded()
    {
        SubmitTriggerZone.StopUnsentRequests();
        if (!State.WaitForSettlement()) return;
        StopPlayerActions();
        Changed?.Invoke();
    }

    internal static bool TryHandleConfirmationMessage(ReadOnlyMemory<byte> data)
    {
        ChatMessagePacket packet = PacketSerializer.Deserialize<ChatMessagePacket>(data);
        string message = packet?.Message;
        if (message == null || !message.StartsWith(ConfirmationPrefix, StringComparison.Ordinal)) return false;
        instance?.ReceivePeerConfirmation(message.Substring(ConfirmationPrefix.Length));
        return true;
    }

    private void ReceivePeerConfirmation(string message)
    {
        StageSettlementData result = State.Settlement;
        if (result == null || selectedPresentation == null || !State.IsInSettlementFlow) return;
        string[] fields = message.Split(':');
        if (fields.Length != 4 || !int.TryParse(fields[0], out int run) || run != result.RunNumber ||
            !int.TryParse(fields[1], out int stage) || stage != result.StageNumber ||
            !int.TryParse(fields[2], out int shot) || shot <= 0 ||
            !room.Lobby.Players.Contains(fields[3])) return;
        if (!shotConfirmations.TryGetValue(shot, out HashSet<string> players))
            shotConfirmations.Add(shot, players = new HashSet<string>());
        players.Add(fields[3]);
        if (State.Phase == StageReturnPhase.FeatureReveal) AdvanceConfirmedShots();
    }

    private bool AllPlayersConfirmed(HashSet<string> players) =>
        room.Lobby.Players.Count > 0 && room.Lobby.Players.All(players.Contains);

    private void AdvanceConfirmedShots()
    {
        if (selectedPresentation == null) return;
        foreach (var confirmation in shotConfirmations)
            if (AllPlayersConfirmed(confirmation.Value))
                selectedPresentation.AdvanceShot(confirmation.Key);
    }

    private async void SendPeerConfirmation(StageSettlementData result, int shot)
    {
        try
        {
            ServerManager server = ServerManager.Instance;
            if (server == null || !server.IsConnected) throw new InvalidOperationException("Server disconnected.");
            // Lobby return has already been confirmed by the server; this message advances presentation only.
            await server.SendData(PacketSerializer.Serialize(new ChatMessagePacket
            {
                Message = $"{ConfirmationPrefix}{result.RunNumber}:{result.StageNumber}:{shot}:{room.Lobby.LocalPlayerId}"
            }));
        }
        catch (Exception error)
        {
            if (State.Settlement == result && !lobbyMovePending)
            {
                if (selectedPresentation != null) selectedPresentation.RetryShot(shot);
            }
            Debug.LogException(error, this);
        }
    }

    private IEnumerator PlayFeatureReveal(FeatureRevealPresentation presentation, StageSettlementData result)
    {
        // Give the settlement camera's LateUpdate a frame to restore the gameplay camera.
        yield return null;
        if (presentation == null || !presentation.TryPlay(result.StageNumber, null,
            shot => SendPeerConfirmation(result, shot)))
        {
            Debug.LogWarning($"[Stage Return] Feature reveal could not start for cleared stage {result.StageNumber}.", this);
        }
        else yield return new WaitUntil(() => !FeatureRevealPresentation.IsPlaying);
        featureReveal = null;
        State.EndFeatureReveal();
        selectedPresentation = null;
        shotConfirmations.Clear();
        Changed?.Invoke();
        LobbyReturnCompleted?.Invoke();
    }

    private void StopFeatureReveal()
    {
        if (featureReveal == null) return;
        StopCoroutine(featureReveal);
        featureReveal = null;
        FeatureRevealPresentation.CancelActive();
        State.EndFeatureReveal();
    }

    public async void ConfirmResult()
    {
        if (!CanConfirmResult) return;
        ServerManager server = ServerManager.Instance;
        if (server == null || !server.IsConnected)
        {
            ConfirmationIssue = "서버 연결을 확인한 뒤 다시 눌러주세요.";
            Changed?.Invoke();
            return;
        }
        if (!State.RequestReturn()) return;
        int run = State.Settlement.RunNumber;
        ConfirmationIssue = null;
        IsConfirming = true;
        Changed?.Invoke();
        try
        {
            await server.SendData(PacketSerializer.Serialize(new StageResultConfirmPacket()));
            if (State.Settlement?.RunNumber == run) ApplyResultConfirmation(room.Lobby.LocalPlayerId);
            Debug.Log($"[Stage Return] C_StageResultConfirm 전송. player={room.Lobby.LocalPlayerId}, id={(ushort)PacketId.C_StageResultConfirm}. S_LobbyMove 대기 중.", this);
        }
        catch (Exception error)
        {
            // Server confirmations are idempotent per player; allow explicit retry after failure.
            if (State.Settlement?.RunNumber == run && !lobbyMovePending && State.CancelReturnRequest())
            {
                ConfirmationIssue = "확인 전송에 실패했습니다. 다시 눌러주세요.";
                Changed?.Invoke();
            }
            Debug.LogException(error, this);
        }
        finally
        {
            if (State.Settlement?.RunNumber == run)
            {
                IsConfirming = false;
                Changed?.Invoke();
            }
        }
    }

    private void OnLobbyMove()
    {
        if (lobbyMovePending || (State.Phase != StageReturnPhase.Settlement &&
            State.Phase != StageReturnPhase.WaitingForServer)) return;
        FeatureRevealPresentation.CancelActive();
        Debug.Log("[Stage Return] S_LobbyMove 수신. 로비 복귀를 시작합니다.", this);
        ConfirmationIssue = null;
        lobbyMovePending = true;
        foreach (string playerId in room.Lobby.Players) confirmedPlayers.Add(playerId);
        Changed?.Invoke();
        TryReturnFromLobbyMove();
    }
    private void Update()
    {
        if (room != null && room.State.HasStageTimeExpired(Time.unscaledTimeAsDouble) && State.WaitForSettlement())
        {
            // Keep accepted submissions and HUD alive until S_StageResult.
            StopPlayerActions();
            Changed?.Invoke();
        }
        if (lobbyMovePending) TryReturnFromLobbyMove();
    }
    private void TryReturnFromLobbyMove()
    {
        PlayerSpawnManager players = FindFirstObjectByType<PlayerSpawnManager>();
        if (players == null)
        {
            ConfirmationIssue = "로비 이동 대기 · PlayerSpawnManager 등록 대기 중";
            return;
        }
        bool ready = players.TryGetLobbyReturnSpawns(room.Lobby.Players,
            out Dictionary<string, Pose> spawns, out string issue);
        if (ready)
        {
            if (ConfirmLobbyReturn(spawns))
            {
                Debug.Log($"[Stage Return] 로비 복귀 시작. players={spawns.Count}, spawnSource=PlayerSpawnManager.spawnRoot", this);
                lobbyMovePending = false;
                lastSpawnIssue = null;
                ConfirmationIssue = null;
                Changed?.Invoke();
            }
            else if (lastSpawnIssue != "플레이어 스폰 등록 대기")
            {
                lastSpawnIssue = "플레이어 스폰 등록 대기";
                ConfirmationIssue = "로비 이동 알림 수신 · 플레이어 등록을 기다리는 중";
                Changed?.Invoke();
                Debug.LogWarning($"[Stage Return] S_LobbyMove 수신 후 플레이어 등록을 기다립니다. lobbyPlayers={room.Lobby.Players.Count}", this);
            }
            return;
        }
        if (issue != lastSpawnIssue)
        {
            lastSpawnIssue = issue;
            ConfirmationIssue = "로비 이동 대기 · " + issue;
            Changed?.Invoke();
            Debug.LogWarning($"[Stage Return] {issue}", this);
        }
    }
    public bool ConfirmLobbyReturn(IReadOnlyDictionary<string, Pose> spawnPoses, bool resetMapRegistration = true)
    {
        if (spawnPoses == null || (State.Phase != StageReturnPhase.Settlement &&
            State.Phase != StageReturnPhase.WaitingForServer)) return false;
        PlayerSpawnManager players = FindFirstObjectByType<PlayerSpawnManager>();
        if (players == null) return false;
        var spawns = new Dictionary<PlayerBrain, Pose>();
        foreach (PlayerBrain player in players.Players)
        {
            if (player == null) continue;
            if (!spawnPoses.TryGetValue(player.PlayerId, out Pose pose))
            {
                Debug.LogWarning($"[Stage Return] 로비 스폰 정보 누락: {player.PlayerId}", this);
                return false;
            }
            spawns.Add(player, pose);
        }
        if (spawns.Count == 0 || spawns.Count != room.Lobby.Players.Count || !State.ConfirmReturn()) return false;
        Changed?.Invoke();
        returning = StartCoroutine(ReturnToLobby(spawns, resetMapRegistration));
        return true;
    }
    private IEnumerator ReturnToLobby(Dictionary<PlayerBrain, Pose> spawns, bool resetMapRegistration)
    {
        bool completed = false;
        try
        {
            // Keep settlement visible while resetting the stage and placing players.
            yield return null;
            ResetLocalStage(resetMapRegistration);
            foreach (var pair in spawns)
            {
                PlayerBrain player = pair.Key;
                if (player == null) continue;
                player.Rocket?.StopFlight();
                player.ActionController?.CancelAction();
                player.ActionController?.ResetForRespawn();
                if (player.StateResolver is LocalPlayerStateResolver local)
                    local.ResetForRespawn();
                if (player.Health != null) player.Health.IsDmgBlocked = false;
                CatchableObj held = player.Interact?.HeldObj;
                if (held != null)
                {
                    player.Interact.TryReleaseHeld(held);
                    held.OnDrop();
                }
                if (player.Input != null) player.Input.ResetForStageReturn();
                if (player.StateResolver is RemotePlayerStateResolver remote)
                    remote.ResetForStageReturn(pair.Value);
                player.transform.SetParent(null, true);
                player.transform.SetPositionAndRotation(pair.Value.position, pair.Value.rotation);
                if (player.Rb != null)
                {
                    player.Rb.position = pair.Value.position;
                    player.Rb.rotation = pair.Value.rotation;
                    if (!player.Rb.isKinematic)
                    {
                        player.Rb.linearVelocity = Vector3.zero;
                        player.Rb.angularVelocity = Vector3.zero;
                    }
                }
            }
            room.NotifyLobbyReturned();
            bool showFeatureReveal = selectedPresentation != null;
            State.CompleteReturn(showFeatureReveal);
            completed = true;
            returning = null;
            if (showFeatureReveal)
                featureReveal = StartCoroutine(PlayFeatureReveal(selectedPresentation, State.Settlement));
            Changed?.Invoke();
            if (!showFeatureReveal) LobbyReturnCompleted?.Invoke();
            Debug.Log("[Stage Return] 로비 복귀 완료. 결산 UI 종료 및 조작 복구.", this);
        }
        finally
        {
            if (!completed)
            {
                State.AbortReturn();
                returning = null;
                Changed?.Invoke();
            }
        }
    }
    private static void PauseOrderPresentation(bool paused)
    {
        foreach (DishOrderManager orders in FindObjectsByType<DishOrderManager>(FindObjectsSortMode.None))
            orders.SetStagePresentationPaused(paused);
    }
    private static void ResetLocalStage(bool resetMapRegistration)
    {
        PauseOrderPresentation(true);
        SubmitTriggerZone.ResetAllClientStageState();
        foreach (IngredientSpawnProcessor processor in FindObjectsByType<IngredientSpawnProcessor>(FindObjectsSortMode.None))
            processor.ClearPending();
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null) continue;
            if (behaviour is IClientStageResettable resettable)
            {
                try { resettable.ResetClientStage(); }
                catch (Exception error) { Debug.LogException(error, behaviour); }
            }
        }
        foreach (ObjectNetworkRouter router in FindObjectsByType<ObjectNetworkRouter>(FindObjectsSortMode.None))
            router.ResetClientStage();
        if (resetMapRegistration)
            foreach (MapObjNetworkRouter router in FindObjectsByType<MapObjNetworkRouter>(FindObjectsSortMode.None))
                router.ResetStageRegistration();
    }

}
