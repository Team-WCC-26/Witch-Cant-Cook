using Protocol;
using Server;
using UnityEngine;
using UnityEngine.InputSystem;

// Test shortcut only. The production HUD remains in StageHudUI.prefab.
public sealed class TEST_SetStage : MonoBehaviour
{
    private static TEST_SetStage instance;
    private StageSelectionSession session;
    private GameObject kitchenDoor;
    private bool startRequested;
    private bool wasRunning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForTestScene()
    {
        if (FindFirstObjectByType<DoorController>() == null || instance != null) return;
        new GameObject(nameof(TEST_SetStage)).AddComponent<TEST_SetStage>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        kitchenDoor = GameObject.Find("KitchenDoor");
    }

    private void OnEnable()
    {
        session = StageSelectionSession.Instance;
        session.StageStarted += OnStageStarted;
        session.Changed += OnSelectionChanged;
        wasRunning = session.IsStageRunning;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            RequestTestStageStart();
    }

    private async void RequestTestStageStart()
    {
        if (StageReturnFlow.IsInputBlocked || session == null || !session.IsRoomReady || !session.HasStageSelection ||
            session.IsStageRunning || startRequested) return;
        if (kitchenDoor == null) kitchenDoor = GameObject.Find("KitchenDoor");
        if (kitchenDoor == null)
        {
            Debug.LogWarning("테스트용 KitchenDoor를 찾을 수 없습니다.", this);
            return;
        }

        startRequested = true;
        bool doorWasActive = kitchenDoor.activeSelf;
        try
        {
            var sendTask = ServerManager.Instance.SendData(PacketSerializer.Serialize(
                new StageStartPacket { StageNum = session.SelectedStageId }));
            kitchenDoor.SetActive(false);
            await sendTask;
        }
        catch (System.Exception exception)
        {
            startRequested = false;
            if (kitchenDoor != null) kitchenDoor.SetActive(doorWasActive);
            Debug.LogException(exception, this);
        }
    }

    private void OnStageStarted(int stageNumber)
    {
        startRequested = true;
        // Preserve the old test path's kitchen-open notifications for tools and stage listeners.
        ServerManager.Instance.DispatchPacket(PacketId.S_OpenDoor,
            MemoryPack.MemoryPackSerializer.Serialize(new OpenDoorPacket { DoorId = DoorId.Kitchen }));
        if (kitchenDoor == null) kitchenDoor = GameObject.Find("KitchenDoor");
        if (kitchenDoor != null) kitchenDoor.SetActive(false);
    }

    private void OnSelectionChanged()
    {
        if (wasRunning && !session.IsStageRunning || !session.IsRoomReady) startRequested = false;
        wasRunning = session.IsStageRunning;
    }

    private void OnDisable()
    {
        if (session == null) return;
        session.StageStarted -= OnStageStarted;
        session.Changed -= OnSelectionChanged;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
