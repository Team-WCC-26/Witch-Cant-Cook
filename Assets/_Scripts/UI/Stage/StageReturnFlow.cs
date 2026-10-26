using System;
using System.Collections;
using System.Collections.Generic;
using Protocol;
using UnityEngine;

// Existing packets only: S_StageStop shows settlement, then S_OpenDoor(Lobby)
// confirms server cleanup and starts return using scene-configured spawn points.
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
    public static bool IsInputBlocked => instance != null && (instance.State.BlocksInput ||
        (instance.room != null && instance.room.State.HasStageTimeExpired(Time.unscaledTimeAsDouble)));
    public static bool IsSettlementActive => instance != null && instance.State.ShowsSettlement;
    public StageReturnState State { get; } = new();
    public event Action Changed;
    public event Action LobbyReturnCompleted;
    private RoomHudSession room;
    private Coroutine returning;
    private bool wasRunning;
    private bool cursorCaptured;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLock;
    private bool lobbyDoorReturnPending;
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
        StageManager.DoorOpened += OnDoorOpened;
        OnRoomChanged();
    }
    private void OnDisable()
    {
        StageManager.DoorOpened -= OnDoorOpened;
        if (room != null)
        {
            room.SettlementRecorded -= OnSettlement;
            room.Changed -= OnRoomChanged;
            room.RoomJoined -= ResetRoom;
        }
        if (returning != null) StopCoroutine(returning);
        returning = null;
        lobbyDoorReturnPending = false;
        State.Reset();
        RestoreCursor();
    }
    private void OnDestroy() { if (instance == this) instance = null; }
    private void ResetRoom()
    {
        if (returning != null) StopCoroutine(returning);
        returning = null;
        wasRunning = false;
        lobbyDoorReturnPending = false;
        lastSpawnIssue = null;
        State.Reset();
        RestoreCursor();
        PauseOrderPresentation(false);
        Changed?.Invoke();
    }
    private void OnRoomChanged()
    {
        bool running = room.State.IsStageRunning;
        if (running && !wasRunning)
        {
            // A genuine server start remains authoritative, including test start paths.
            if (returning != null) StopCoroutine(returning);
            returning = null;
            lobbyDoorReturnPending = false;
            State.StartStage();
            RestoreCursor();
            PauseOrderPresentation(false);
            Changed?.Invoke();
        }
        wasRunning = running;
    }
    private void OnSettlement(StageSettlementData data)
    {
        if (!State.RecordSettlement(data)) return;
        State.RequestReturn();
        Debug.Log("[Stage Return] 결산 수신. S_OpenDoor(Lobby)를 기다립니다.", this);
        if (!cursorCaptured)
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            cursorCaptured = true;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PauseOrderPresentation(true);
        // Final totals are authoritative. Plate return animations may finish independently.
        StopPlayerActions();
        Changed?.Invoke();
    }
    private static void StopPlayerActions()
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
    private void OnDoorOpened(DoorId door)
    {
        if (door == DoorId.Lobby)
            Debug.Log($"[Stage Return] 로비 문 알림 전달됨. phase={State.Phase}", this);
        if (door != DoorId.Lobby || (State.Phase != StageReturnPhase.Settlement &&
            State.Phase != StageReturnPhase.WaitingForServer)) return;
        lobbyDoorReturnPending = true;
        TryReturnFromLobbyDoor();
    }
    private void Update()
    {
        if (room != null && room.State.HasStageTimeExpired(Time.unscaledTimeAsDouble) && State.WaitForSettlement())
        {
            // Keep the stage/HUD and accepted submission queue alive until S_StageStop.
            StopPlayerActions();
            Changed?.Invoke();
        }
        if (lobbyDoorReturnPending) TryReturnFromLobbyDoor();
    }
    private void TryReturnFromLobbyDoor()
    {
        LobbyReturnSpawnPoints config = FindFirstObjectByType<LobbyReturnSpawnPoints>();
        string issue = null;
        if (config == null) issue = "씬에 LobbyReturnSpawnPoints를 추가하고 로비 스폰 위치를 지정하세요.";
        else if (config.TryGetSpawns(room.Lobby.Players, out Dictionary<string, Pose> spawns, out issue))
        {
            if (ConfirmLobbyReturn(spawns, config.ResetMapRegistration))
            {
                Debug.Log($"[Stage Return] 로비 복귀 시작. players={spawns.Count}", this);
                lobbyDoorReturnPending = false;
                lastSpawnIssue = null;
            }
            else if (lastSpawnIssue != "플레이어 스폰 등록 대기")
            {
                lastSpawnIssue = "플레이어 스폰 등록 대기";
                Debug.LogWarning($"[Stage Return] 로비 문 알림은 수신했지만 플레이어 등록을 기다립니다. lobbyPlayers={room.Lobby.Players.Count}", this);
            }
            return;
        }
        if (issue != lastSpawnIssue)
        {
            lastSpawnIssue = issue;
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
                player.Health?.Reset();
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
            StageSettlementUI ui = FindFirstObjectByType<StageSettlementUI>();
            if (ui != null) yield return ui.FadeOut();
            RestoreCursor();
            State.CompleteReturn();
            completed = true;
            returning = null;
            Changed?.Invoke();
            LobbyReturnCompleted?.Invoke();
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

    private void RestoreCursor()
    {
        if (!cursorCaptured) return;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        cursorCaptured = false;
    }
}
