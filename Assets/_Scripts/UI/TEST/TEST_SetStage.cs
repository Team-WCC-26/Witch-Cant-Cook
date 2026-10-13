using Protocol;
using Server;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Tab으로 현재 선택된 스테이지를 시작하는 테스트 UI입니다.
/// StageManager가 있는 씬에서 자동 생성되며 스테이지 선택은 World Canvas에서 합니다.
/// </summary>
public class TEST_SetStage : MonoBehaviour
{
    private static TEST_SetStage instance;
    private const float StageDurationSeconds = 240f;

    private TMP_Text currentStageText;
    private TMP_Text remainingTimeText;
    private GameObject canvasObject;
    private StageSelectionSession stageSession;
    private double stageEndTime;
    private bool isTimerRunning;
    private bool testStageStartRequested;
    private bool stageWasRunning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForTestScene()
    {
        if (Object.FindFirstObjectByType<StageManager>() == null || instance != null) return;
        new GameObject(nameof(TEST_SetStage)).AddComponent<TEST_SetStage>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void Start()
    {
        CreateUi();
        stageSession = StageSelectionSession.Instance;
        stageSession.StageStarted += OnStageStarted;
        stageSession.Changed += OnSelectionChanged;
        OnSelectionChanged();
        ShowRemainingTime(StageDurationSeconds);
        StageManager.DoorOpened += OnDoorOpened;
        if (stageSession.IsStageRunning) RestartTimer();
    }

    private void Update()
    {
        UpdateTimer();
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            RequestTestStageStart();
    }

    private void OnDestroy()
    {
        StageManager.DoorOpened -= OnDoorOpened;
        if (instance == this) instance = null;
        if (stageSession != null)
        {
            stageSession.StageStarted -= OnStageStarted;
            stageSession.Changed -= OnSelectionChanged;
        }
        if (canvasObject != null) Destroy(canvasObject);
    }

    private void OnSelectionChanged()
    {
        if (currentStageText != null)
            currentStageText.text = $"현재 스테이지: {stageSession.SelectedStageId}";
        if (stageWasRunning && !stageSession.IsStageRunning)
            testStageStartRequested = false;
        stageWasRunning = stageSession.IsStageRunning;
    }

    private void OnStageStarted(int stageNumber)
    {
        testStageStartRequested = true;
        // 테스트용 로컬 알림: 기존 문 처리와 주방 도구 등록 이벤트를 함께 실행합니다.
        ServerManager.Instance.DispatchPacket(PacketId.S_OpenDoor,
            MemoryPack.MemoryPackSerializer.Serialize(new OpenDoorPacket { DoorId = DoorId.Kitchen }));
        RestartTimer();
        GameObject.Find("KitchenDoor")?.SetActive(false);
    }

    private async void RequestTestStageStart()
    {
        if (stageSession == null || testStageStartRequested || stageSession.IsStageRunning) return;
        var server = ServerManager.Instance;
        if (server == null || !stageSession.IsRoomReady)
        {
            Debug.LogWarning("방에 입장한 뒤 Tab을 눌러주세요.");
            return;
        }

        GameObject kitchenDoor = GameObject.Find("KitchenDoor");
        if (kitchenDoor == null)
        {
            Debug.LogWarning("테스트용 KitchenDoor를 찾을 수 없습니다.");
            return;
        }

        testStageStartRequested = true;
        try
        {
            var sendTask = server.SendData(PacketSerializer.Serialize(new StageStartPacket
            {
                StageNum = stageSession.SelectedStageId
            }));
            // 요청과 동시에 문과 자식 콜라이더를 비활성화해 통행할 수 있게 합니다.
            kitchenDoor.SetActive(false);
            await sendTask;
        }
        catch (System.Exception exception)
        {
            testStageStartRequested = false;
            if (kitchenDoor != null) kitchenDoor.SetActive(true);
            Debug.LogException(exception);
        }
    }

    private void OnDoorOpened(DoorId doorId)
    {
        if (doorId == DoorId.Kitchen && !isTimerRunning) RestartTimer();
    }

    private void RestartTimer()
    {
        stageEndTime = Time.unscaledTimeAsDouble + StageDurationSeconds;
        isTimerRunning = true;
        ShowRemainingTime(StageDurationSeconds);
    }

    private void UpdateTimer()
    {
        if (!isTimerRunning) return;
        double remaining = stageEndTime - Time.unscaledTimeAsDouble;
        if (remaining > 0d) { ShowRemainingTime((float)remaining); return; }
        isTimerRunning = false;
        testStageStartRequested = false;
        remainingTimeText.text = "스테이지 종료!";

        PlayerSpawnManager spawnManager = PlayerSpawnManager.Instance;
        if (spawnManager == null) return;
        foreach (PlayerBrain player in spawnManager.Players)
            if (player != null) spawnManager.RespawnPlayer(player.PlayerId);
    }

    private void ShowRemainingTime(float seconds)
    {
        int totalSeconds = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        remainingTimeText.text = $"{totalSeconds / 60}:{totalSeconds % 60:00}";
    }

    private void CreateUi()
    {
        canvasObject = new GameObject("Stage Test Canvas", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        currentStageText = CreateText("Current Stage", 32, -28f, 48f);
        remainingTimeText = CreateText("Stage Remaining Time", 28, -78f, 44f);
    }

    private TMP_Text CreateText(string objectName, float size, float y, float height)
    {
        GameObject textObject = new(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(canvasObject.transform, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Top;
        text.color = Color.white;
        text.raycastTarget = false;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(380f, height);
        return text;
    }
}
