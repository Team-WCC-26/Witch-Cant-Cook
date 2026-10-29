using System.Globalization;
using Cysharp.Threading.Tasks;
using Server;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class LobbyHudUI : UIBase
{
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private Button codeVisibilityButton;
    [SerializeField] private GameObject[] participantRows = System.Array.Empty<GameObject>();
    [SerializeField] private TMP_Text[] participantNames = System.Array.Empty<TMP_Text>();
    [SerializeField] private Image[] voiceIcons = System.Array.Empty<Image>();
    [SerializeField] private Sprite speakingIcon;
    [SerializeField] private Sprite idleIcon;
    [Header("Return to title (lobby only)")]
    [SerializeField] private GameObject returnConfirmation;
    [SerializeField] private TMP_Text returnConfirmationText;
    [SerializeField] private Button confirmReturnButton;
    [SerializeField] private Button cancelReturnButton;
    private static LobbyHudUI modalOwner;
    private static bool titleTransitionInProgress;
    public static bool IsInputBlocked => modalOwner != null || titleTransitionInProgress;
    private bool returningToTitle;
    private RoomHudSession session;
    private bool showCode = true;

    private void OnEnable()
    {
        session = RoomHudSession.Instance;
        session.Changed += Refresh;
        if (codeVisibilityButton != null) codeVisibilityButton.onClick.AddListener(ToggleCode);
        if (confirmReturnButton != null) confirmReturnButton.onClick.AddListener(ConfirmReturn);
        if (cancelReturnButton != null) cancelReturnButton.onClick.AddListener(CancelReturn);
        ConfigureReturnButton(confirmReturnButton, "확인 [Space]");
        ConfigureReturnButton(cancelReturnButton, "취소 [ESC]");
        if (returnConfirmation != null) returnConfirmation.SetActive(false);
        returningToTitle = false;
        Refresh();
    }

    private void OnDisable()
    {
        if (session != null) session.Changed -= Refresh;
        if (codeVisibilityButton != null) codeVisibilityButton.onClick.RemoveListener(ToggleCode);
        if (confirmReturnButton != null) confirmReturnButton.onClick.RemoveListener(ConfirmReturn);
        if (cancelReturnButton != null) cancelReturnButton.onClick.RemoveListener(CancelReturn);
        CloseReturnConfirmation();
        session = null;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (session == null || keyboard == null) return;
        if (modalOwner == this)
        {
            if (keyboard.escapeKey.wasPressedThisFrame) CancelReturn();
            else if (keyboard.spaceKey.wasPressedThisFrame) ConfirmReturn();
            return;
        }
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            ShowReturnConfirmation();
            return;
        }
        if (keyboard.qKey.wasPressedThisFrame)
        {
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            TMP_InputField input = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
            if (input == null || !input.isFocused) ToggleCode();
        }
    }

    private void ToggleCode() { showCode = !showCode; Refresh(); }

    private static void ConfigureReturnButton(Button button, string label)
    {
        if (button == null) return;
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = label;
        // Space/Escape are handled by the popup, independently of UI Submit focus.
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
    }

    private void ShowReturnConfirmation()
    {
        if (titleTransitionInProgress || returningToTitle || modalOwner != null || returnConfirmation == null ||
            session == null || !session.Lobby.IsActive) return;
        modalOwner = this;
        returnConfirmationText.text = "타이틀로 돌아가시겠습니까?";
        confirmReturnButton.interactable = true;
        cancelReturnButton.interactable = true;
        returnConfirmation.SetActive(true);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void CancelReturn()
    {
        if (!returningToTitle) CloseReturnConfirmation();
    }

    private void CloseReturnConfirmation()
    {
        if (returnConfirmation != null) returnConfirmation.SetActive(false);
        if (modalOwner != this) return;
        modalOwner = null;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private async void ConfirmReturn()
    {
        if (returningToTitle || modalOwner != this) return;
        returningToTitle = true;
        titleTransitionInProgress = true;
        confirmReturnButton.interactable = false;
        cancelReturnButton.interactable = false;
        try
        {
            const string titleScene = "TitleScene";
            if (!Application.CanStreamedLevelBeLoaded(titleScene))
                throw new System.InvalidOperationException("TitleScene is missing from the build scene list.");
            ServerManager server = ServerManager.Instance;
            if (server != null) await server.ExitRoomAsync();
            StageSelectionSession.Instance.OnRoomLeft();
            await SceneManager.LoadSceneAsync(titleScene);
            // Pooled objects survive scene changes; discard the previous room's live objects.
            if (ObjectPoolManager.Instance != null) ObjectPoolManager.Instance.ReturnRoomObjects();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            if (this == null || !isActiveAndEnabled) return;
            returningToTitle = false;
            returnConfirmationText.text = "타이틀로 돌아가지 못했습니다.\n다시 시도해 주세요.";
            confirmReturnButton.interactable = true;
            cancelReturnButton.interactable = true;
        }
        finally { titleTransitionInProgress = false; }
    }

    private void Refresh()
    {
        if (session == null) return;
        LobbyHudState lobby = session.Lobby;
        if (roomCodeText != null) roomCodeText.text = showCode ? lobby.RoomCode : "******";
        if (moneyText != null) moneyText.text = "보유 황금 콩 - " +
            (session.State.HasMoney ? session.State.Money.ToString("N0", CultureInfo.InvariantCulture) : "--");
        for (int i = 0; i < participantRows.Length; i++)
        {
            bool occupied = i < lobby.Players.Count;
            if (participantRows[i] != null) participantRows[i].SetActive(occupied);
            if (i < participantNames.Length && participantNames[i] != null)
                participantNames[i].text = $"참가자 {i + 1}";
            if (i < voiceIcons.Length && voiceIcons[i] != null)
                voiceIcons[i].sprite = occupied && lobby.IsSpeaking(lobby.Players[i]) ? speakingIcon : idleIcon;
        }
    }
}
