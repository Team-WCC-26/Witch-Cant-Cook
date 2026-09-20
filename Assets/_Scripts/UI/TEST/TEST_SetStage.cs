using Protocol;
using Server;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 스테이지 테스트를 위한 런타임 UI입니다.
/// StageManager가 있는 씬에서 자동으로 생성되고 S_StageSet 응답으로 표시를 갱신합니다.
/// </summary>
public class TEST_SetStage : MonoBehaviour
{
    private static TEST_SetStage instance;

    private TMP_Text currentStageText;
    private GameObject stageInputPanel;
    private TMP_InputField stageInput;
    private TMP_Text validationText;

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
        RegisterStagePacketHandler();
        UpdateCurrentStage(1);
    }
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
        {
            OpenInputPanel();
        }
    }
    private void OnDestroy()
    {
        if (instance == this) instance = null;

        if (ServerManager.Instance != null)
        {
            ServerManager.Instance.UnRegisterHandler(PacketId.S_StageSet);
        }
    }

    private void RegisterStagePacketHandler()
    {
        if (ServerManager.Instance == null) return;

        ServerManager.Instance.RegisterHandler(PacketId.S_StageSet, data =>
        {
            StageSetPacket packet = PacketSerializer.Deserialize<StageSetPacket>(data);
            UpdateCurrentStage(packet.StageNum);
        });
    }

    private void CreateUi()
    {
        GameObject canvasObject = new("Stage Test Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        CreateEventSystemIfNeeded();

        currentStageText = CreateText(canvasObject.transform, "Current Stage", 32, TextAnchor.UpperCenter);
        SetAnchors(currentStageText.rectTransform, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -28f), new Vector2(380f, 48f));

        Button openButton = CreateButton(canvasObject.transform, "Change Stage Button", "스테이지 변경");
        SetAnchors(openButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-120f, -38f), new Vector2(200f, 52f));
        openButton.onClick.AddListener(OpenInputPanel);

        stageInputPanel = CreatePanel(canvasObject.transform, "Stage Input Panel", new Color(0f, 0f, 0f, .82f));
        SetAnchors(stageInputPanel.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(440f, 230f));

        TMP_Text title = CreateText(stageInputPanel.transform, "Title", 28, TextAnchor.MiddleCenter);
        title.text = "이동할 스테이지 번호";
        SetAnchors(title.rectTransform, new Vector2(.5f, 1f), new Vector2(.5f, 1f), new Vector2(0f, -42f), new Vector2(380f, 38f));

        stageInput = CreateInputField(stageInputPanel.transform);
        SetAnchors(stageInput.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0f, 18f), new Vector2(290f, 48f));

        validationText = CreateText(stageInputPanel.transform, "Validation", 18, TextAnchor.MiddleCenter);
        validationText.color = new Color(1f, .45f, .45f);
        SetAnchors(validationText.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0f, -30f), new Vector2(350f, 28f));

        Button confirmButton = CreateButton(stageInputPanel.transform, "Confirm Button", "확인");
        SetAnchors(confirmButton.GetComponent<RectTransform>(), new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(-76f, 38f), new Vector2(130f, 46f));
        confirmButton.onClick.AddListener(RequestStageChange);

        Button cancelButton = CreateButton(stageInputPanel.transform, "Cancel Button", "취소");
        SetAnchors(cancelButton.GetComponent<RectTransform>(), new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(76f, 38f), new Vector2(130f, 46f));
        cancelButton.onClick.AddListener(CloseInputPanel);

        stageInput.onSubmit.AddListener(_ => RequestStageChange());
        stageInputPanel.SetActive(false);
    }

    private void OpenInputPanel()
    {
        validationText.text = string.Empty;
        stageInput.text = string.Empty;
        stageInputPanel.SetActive(true);
        stageInput.ActivateInputField();
    }

    private void CloseInputPanel()
    {
        stageInputPanel.SetActive(false);
    }

    private async void RequestStageChange()
    {
        if (!int.TryParse(stageInput.text, out int stageNumber) || stageNumber < 1)
        {
            validationText.text = "1 이상의 스테이지 번호를 입력하세요.";
            return;
        }

        if (ServerManager.Instance == null)
        {
            validationText.text = "서버 연결을 찾을 수 없습니다.";
            return;
        }

        await ServerManager.Instance.SendData(PacketSerializer.Serialize(new StageSetPacket
        {
            StageNum = stageNumber
        }));

        CloseInputPanel();
    }

    private void UpdateCurrentStage(int stageNumber)
    {
        if (currentStageText != null) currentStageText.text = $"현재 스테이지: {stageNumber}";
    }

    private static GameObject CreatePanel(Transform parent, string objectName, Color color)
    {
        GameObject panel = new(objectName, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    private static Button CreateButton(Transform parent, string objectName, string label)
    {
        GameObject buttonObject = CreatePanel(parent, objectName, new Color(.20f, .38f, .62f, .98f));
        Button button = buttonObject.AddComponent<Button>();

        TMP_Text text = CreateText(buttonObject.transform, "Label", 22, TextAnchor.MiddleCenter);
        text.text = label;
        SetAnchors(text.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1000f, 1000f));
        return button;
    }

    private static TMP_InputField CreateInputField(Transform parent)
    {
        GameObject inputObject = CreatePanel(parent, "Stage Number Input", Color.white);
        TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 5;

        TMP_Text placeholder = CreateText(inputObject.transform, "Placeholder", 22, TextAnchor.MiddleLeft);
        placeholder.text = "스테이지 번호 입력";
        placeholder.color = new Color(.35f, .35f, .35f, .7f);
        SetAnchors(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 0f), new Vector2(-16f, 0f));

        TMP_Text text = CreateText(inputObject.transform, "Text", 22, TextAnchor.MiddleLeft);
        text.color = Color.black;
        SetAnchors(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(16f, 0f), new Vector2(-16f, 0f));

        input.targetGraphic = inputObject.GetComponent<Image>();
        input.textViewport = inputObject.GetComponent<RectTransform>();
        input.textComponent = text;
        input.placeholder = placeholder;
        return input;
    }

    private static TMP_Text CreateText(Transform parent, string objectName, float size, TextAnchor alignment)
    {
        GameObject textObject = new(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = ConvertAlignment(alignment);
        text.color = Color.white;
        return text;
    }

    private static TextAlignmentOptions ConvertAlignment(TextAnchor alignment)
    {
        return alignment switch
        {
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.MiddleLeft => TextAlignmentOptions.MidlineLeft,
            _ => TextAlignmentOptions.Center,
        };
    }

    private static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void CreateEventSystemIfNeeded()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        GameObject eventSystem = new("EventSystem", typeof(EventSystem));
        eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }
}
