using System.Globalization;
using TMPro;
using UnityEngine;

// Displays the HUD prefab and its stage-start slide animation.
public sealed class StageHudUI : UIBase
{
    [SerializeField] private TMP_Text remainingTimeText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text moneyText;
    [Header("Stage Start Slide")]
    [SerializeField] private RectTransform clockIcon;
    [Tooltip("시계 다음에 들어올 영역. 배열 순서로 조금씩 늦게 시작합니다.")]
    [SerializeField] private RectTransform[] informationPanels = System.Array.Empty<RectTransform>();
    [SerializeField, Min(0f)] private float slideDistance = 600f;
    [SerializeField, Min(.01f)] private float clockSlideDuration = .35f;
    [SerializeField, Min(0f)] private float informationDelay = .2f;
    [SerializeField, Min(.01f)] private float informationSlideDuration = .4f;
    [SerializeField, Min(0f)] private float panelStagger = .06f;
    private RoomHudSession session;
    private int displayedSeconds = -1;
    private Vector2 clockShownPosition;
    private Vector2[] panelShownPositions;
    private bool positionsCached;
    private bool wasStageRunning;
    private int previousStageNumber;
    private bool introPlaying;
    private float introElapsed;

    private void OnEnable()
    {
        session = RoomHudSession.Instance;
        session.Changed += Refresh;
        bool firstOpen = !positionsCached;
        CachePositions();
        RestorePositions();
        // A newly loaded HUD can receive the start after the session already changed.
        // Reopening an existing HUD mid-stage still avoids replaying the intro.
        wasStageRunning = session.State.IsStageRunning && !firstOpen;
        previousStageNumber = session.State.StageNumber;
        Refresh();
    }

    private void OnDisable()
    {
        if (session != null) session.Changed -= Refresh;
        RestorePositions();
        session = null;
    }

    public override void Opened(object[] param)
    {
        if (param.Length == 0 || param[0] is not bool playIntro || !playIntro || introPlaying ||
            session == null || !session.State.IsStageRunning) return;
        introElapsed = 0f;
        introPlaying = true;
        ApplySlidePositions();
    }

    private void Update()
    {
        RefreshClock();
        if (!introPlaying) return;
        introElapsed += Time.unscaledDeltaTime;
        ApplySlidePositions();
    }

    private void Refresh()
    {
        RoomHudState state = session.State;
        bool started = state.IsStageRunning && (!wasStageRunning || previousStageNumber != state.StageNumber);
        wasStageRunning = state.IsStageRunning;
        previousStageNumber = state.StageNumber;
        if (started)
        {
            introElapsed = 0f;
            introPlaying = true;
            ApplySlidePositions();
        }
        else if (!state.IsStageRunning) RestorePositions();
        if (roundText != null)
            roundText.text = state.HasStageSelection ? $"Round - {state.StageNumber}" : "Round - --";
        if (moneyText != null)
            moneyText.text = "획득 황금 콩 - " + (state.HasMoney ? state.Money.ToString("N0", CultureInfo.InvariantCulture) : "--");
        displayedSeconds = -1;
        RefreshClock();
    }

    private void RefreshClock()
    {
        if (session == null || remainingTimeText == null) return;
        if (!session.State.HasStageSelection)
        {
            if (displayedSeconds != -2) remainingTimeText.text = "남은 시간 - --:--";
            displayedSeconds = -2;
            return;
        }
        int seconds = session.State.RemainingSeconds(Time.unscaledTimeAsDouble);
        if (displayedSeconds == seconds) return;
        displayedSeconds = seconds;
        remainingTimeText.text = "남은 시간 - " + RoomHudState.FormatTime(seconds);
    }

    private void CachePositions()
    {
        if (positionsCached) return;
        if (clockIcon != null) clockShownPosition = clockIcon.anchoredPosition;
        panelShownPositions = new Vector2[informationPanels.Length];
        for (int i = 0; i < informationPanels.Length; i++)
            if (informationPanels[i] != null) panelShownPositions[i] = informationPanels[i].anchoredPosition;
        positionsCached = true;
    }

    private void ApplySlidePositions()
    {
        Slide(clockIcon, clockShownPosition, 0f, clockSlideDuration);
        for (int i = 0; i < informationPanels.Length; i++)
            Slide(informationPanels[i], panelShownPositions[i], informationDelay + i * panelStagger,
                informationSlideDuration);
        float panelsEnd = informationPanels.Length > 0
            ? informationDelay + (informationPanels.Length - 1) * panelStagger + informationSlideDuration : 0f;
        if (introElapsed >= Mathf.Max(clockSlideDuration, panelsEnd)) RestorePositions();
    }

    private void Slide(RectTransform target, Vector2 shown, float delay, float duration)
    {
        if (target == null) return;
        float progress = Mathf.Clamp01((introElapsed - delay) / Mathf.Max(.01f, duration));
        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        float hiddenX = Mathf.Min(shown.x - slideDistance, -target.rect.width * (1f - target.pivot.x) - 24f);
        target.anchoredPosition = new Vector2(Mathf.Lerp(hiddenX, shown.x, eased), shown.y);
    }

    private void RestorePositions()
    {
        introPlaying = false;
        if (!positionsCached) return;
        if (clockIcon != null) clockIcon.anchoredPosition = clockShownPosition;
        for (int i = 0; i < informationPanels.Length; i++)
            if (informationPanels[i] != null) informationPanels[i].anchoredPosition = panelShownPositions[i];
    }
}
