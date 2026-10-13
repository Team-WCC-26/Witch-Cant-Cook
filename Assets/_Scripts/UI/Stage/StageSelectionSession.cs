using System;
using Cysharp.Threading.Tasks;
using Protocol;
using Server;
using UnityEngine;

// UI-owned stage packet receiver. Uses the existing ServerManager API unchanged.
// Boards and test UI share this receiver instead of registering competing handlers.
public sealed class StageSelectionSession : MonoBehaviour
{
    private static StageSelectionSession instance;
    private ServerManager server;
    private PlayerSpawnManager players;

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
    public bool IsStageRunning { get; private set; }
    public bool IsRoomReady => server != null && players != null &&
        !string.IsNullOrEmpty(players.MyID) && players.ContainsPlayer(players.MyID);

    public event Action Changed;
    public event Action<int> StageSelected;
    public event Action<int> StageStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize() => _ = Instance;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    private void OnEnable() => StageManager.DoorOpened += OnDoorOpened;

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
        players = FindFirstObjectByType<PlayerSpawnManager>();
    }

    // Called by the existing lobby UI, which already owns S_JoinRoom.
    // Door state comes from StageManager.DoorOpened, so neither handler is replaced.
    public void OnRoomJoined()
    {
        SelectedStageId = 1;
        IsStageRunning = false;
        Changed?.Invoke();
    }

    public async UniTask RequestSelection(int stageId)
    {
        TryRegister();
        if (!IsRoomReady) throw new InvalidOperationException("Join a room before selecting a stage.");
        await server.SendData(PacketSerializer.Serialize(new StageSetPacket { StageNum = stageId }));
    }

    private void OnStageSet(ReadOnlyMemory<byte> bytes)
    {
        SelectedStageId = PacketSerializer.Deserialize<StageSetPacket>(bytes).StageNum;
        Changed?.Invoke();
        StageSelected?.Invoke(SelectedStageId);
    }

    private void OnStageStart(ReadOnlyMemory<byte> bytes)
    {
        SelectedStageId = PacketSerializer.Deserialize<StageStartPacket>(bytes).StageNum;
        IsStageRunning = true;
        Changed?.Invoke();
        StageStarted?.Invoke(SelectedStageId);
    }

    private void OnStageStop(ReadOnlyMemory<byte> bytes)
    {
        IsStageRunning = false;
        Changed?.Invoke();
    }

    private void OnDoorOpened(DoorId doorId)
    {
        if (doorId != DoorId.Kitchen) return;
        IsStageRunning = true;
        Changed?.Invoke();
    }

    private void OnDisable()
    {
        StageManager.DoorOpened -= OnDoorOpened;
        if (server != null)
        {
            server.UnRegisterHandler(PacketId.S_StageSet);
            server.UnRegisterHandler(PacketId.S_StageStart);
            server.UnRegisterHandler(PacketId.S_StageStop);
        }
        server = null;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
