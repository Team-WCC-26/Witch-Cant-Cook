using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class LobbyHudUI : UIBase
{
    [SerializeField] private TMP_Text remainingTimeText;
    [SerializeField] private TMP_Text roomCodeText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private Button codeVisibilityButton;
    [SerializeField] private GameObject[] participantRows = System.Array.Empty<GameObject>();
    [SerializeField] private TMP_Text[] participantNames = System.Array.Empty<TMP_Text>();
    [SerializeField] private Image[] voiceIcons = System.Array.Empty<Image>();
    [SerializeField] private Sprite speakingIcon;
    [SerializeField] private Sprite idleIcon;
    [Header("Lobby Countdown")]
    [SerializeField, Min(0f)] private float durationSeconds = 30f;
    private RoomHudSession session;
    private bool showCode = true;
    private int displayedSeconds = -1;

    private void OnEnable()
    {
        session = RoomHudSession.Instance;
        session.Changed += Refresh;
        session.SetLobbyDuration(durationSeconds);
        if (codeVisibilityButton != null) codeVisibilityButton.onClick.AddListener(ToggleCode);
        Refresh();
    }

    private void OnDisable()
    {
        if (session != null) session.Changed -= Refresh;
        if (codeVisibilityButton != null) codeVisibilityButton.onClick.RemoveListener(ToggleCode);
        session = null;
    }

    private void Update()
    {
        if (session == null) return;
        int seconds = session.Lobby.RemainingSeconds(Time.unscaledTimeAsDouble);
        if (remainingTimeText != null && displayedSeconds != seconds)
            remainingTimeText.text = RoomHudState.FormatTime(seconds);
        displayedSeconds = seconds;
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
        {
            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            TMP_InputField input = selected != null ? selected.GetComponentInParent<TMP_InputField>() : null;
            if (input == null || !input.isFocused) ToggleCode();
        }
    }

    private void ToggleCode() { showCode = !showCode; Refresh(); }

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
        displayedSeconds = -1;
    }
}
