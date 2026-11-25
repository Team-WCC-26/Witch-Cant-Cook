using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Displays the HUD prefab and its stage-start slide animation.
public sealed class StageHudUI : UIBase
{
    [SerializeField] private TMP_Text remainingTimeText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private Slider goalProgress;
    [SerializeField] private TMP_Text progressText;
    [SerializeField, Min(.01f)] private float progressFillDuration = .35f;
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
    public string MoneyDisplayText => moneyText != null ? moneyText.text : "moneyText 미연결";
    private int displayedSeconds = -1;
    private Vector2 clockShownPosition;
    private Vector2[] panelShownPositions;
    private bool positionsCached;
    private bool wasStageRunning;
    private int previousStageNumber;
    private bool introPlaying;
    private float introElapsed;
    private DataManager stageData;
    private int goalStageNumber = -1;
    private bool goalResolved;
    private int? stageGoal;
    private float targetProgress;

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
        if (session != null && !goalResolved && TryResolveGoal(session.State))
            RefreshMoney(session.State);
        if (goalProgress != null)
            goalProgress.SetValueWithoutNotify(Mathf.MoveTowards(goalProgress.value, targetProgress,
                Time.unscaledDeltaTime / Mathf.Max(.01f, progressFillDuration)));
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
        {
            roundText.text = state.HasStageSelection ? $"스테이지 {state.StageNumber:00}" : "스테이지 --";
            StageSettlementData result = state.LatestSettlement;
            if (!state.IsStageRunning && result != null && result.StageNumber == state.StageNumber)
                roundText.text += result.IsSuccess ? " / 성공" : " / 실패";
        }
        RefreshMoney(state);
        displayedSeconds = -1;
        RefreshClock();
    }


    private bool TryResolveGoal(RoomHudState state)
    {
        if (goalStageNumber != state.StageNumber)
        {
            goalStageNumber = state.StageNumber;
            goalResolved = false;
            stageGoal = null;
            targetProgress = 0f;
            if (goalProgress != null) goalProgress.SetValueWithoutNotify(0f);
        }
        if (goalResolved || !state.HasStageSelection) return false;
        if (stageData == null) stageData = FindFirstObjectByType<DataManager>();
        if (stageData == null || !stageData.IsDataLoaded) return false;
        goalResolved = true;
        foreach (Stage stage in stageData.GetStage().GetAll())
            if (stage != null && stage.stageNum == state.StageNumber)
            {
                if (stage.goal >= 0) stageGoal = stage.goal;
                break;
            }
        return true;
    }

    private void RefreshMoney(RoomHudState state)
    {
        TryResolveGoal(state);
        string earned = state.HasPendingMoney
            ? state.PendingMoney.ToString("N0", CultureInfo.InvariantCulture) : "--";
        bool hasGoal = state.HasStageSelection && stageGoal.HasValue;
        string goal = hasGoal ? stageGoal.Value.ToString("N0", CultureInfo.InvariantCulture) : "--";
        if (moneyText != null)
            moneyText.text = goalProgress != null
                ? earned + "<size=70%><color=#DAC5A6> / " + goal + "</color></size>"
                : "<size=18>" + (state.HasActiveStage ? "이번 판 획득 예정" : "이번 판 획득") +
                    "</size>\n<size=38><color=#FFE3A0>" + earned + "</color></size>";
        bool hasProgress = hasGoal && state.HasPendingMoney;
        targetProgress = !hasProgress ? 0f : stageGoal.Value == 0 ? 1f
            : Mathf.Clamp01((float)state.PendingMoney / stageGoal.Value);
        if (progressText != null)
        {
            progressText.text = !hasProgress ? "--%" : targetProgress >= 1f ? "목표 달성"
                : Mathf.FloorToInt(targetProgress * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            progressText.color = hasProgress && targetProgress >= 1f
                ? new Color(.8f, 1f, .56f) : new Color(1f, .89f, .63f);
        }
    }

    private void RefreshClock()
    {
        if (session == null || remainingTimeText == null) return;
        if (!session.State.HasStageSelection)
        {
            if (displayedSeconds != -2) remainingTimeText.text = "--:--";
            displayedSeconds = -2;
            return;
        }
        int seconds = session.State.RemainingSeconds(Time.unscaledTimeAsDouble);
        if (displayedSeconds == seconds) return;
        displayedSeconds = seconds;
        remainingTimeText.text = RoomHudState.FormatTime(seconds);
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
        float hiddenX = target.anchorMin.x > .5f
            ? Mathf.Max(shown.x + slideDistance, target.rect.width * target.pivot.x + 24f)
            : Mathf.Min(shown.x - slideDistance, -target.rect.width * (1f - target.pivot.x) - 24f);
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


