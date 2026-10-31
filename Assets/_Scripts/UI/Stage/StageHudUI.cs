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
    [Header("플레이어 체력")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private RectTransform healthDamageTrail;
    [SerializeField, Min(0f), InspectorName("잔상 유지 시간")] private float healthTrailDelay = .25f;
    [SerializeField, Min(.01f), InspectorName("잔상 감소 시간")] private float healthTrailDuration = .45f;
    private float previousHealthFill, healthTrailStart, healthTrailHoldRemaining, healthTrailElapsed;
    private PlayerSpawnManager playerManager;
    private PlayerHealthData health;
    private PlayerBrain healthPlayer;
    private bool showingDeathRecovery;
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
        BindLocalHealthIfNeeded();
        RefreshHealth(health);
    }

    private void OnDisable()
    {
        BindHealth(null);
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
        BindLocalHealthIfNeeded();
        TickHealthRecovery();
        TickHealthFeedback(Time.unscaledDeltaTime);
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

    // 피해와 부활의 서버 확정 체력을 구독하며 HUD에서는 체력을 변경하지 않는다.
    private void BindLocalHealthIfNeeded()
    {
        if (playerManager == null) playerManager = FindFirstObjectByType<PlayerSpawnManager>();
        PlayerHealthData nextHealth = null;
        healthPlayer = null;
        if (playerManager != null && !string.IsNullOrEmpty(playerManager.MyID) &&
            playerManager.TryGetPlayer(playerManager.MyID, out PlayerBrain localPlayer) && localPlayer != null)
        {
            healthPlayer = localPlayer;
            nextHealth = localPlayer.Health;
        }
        BindHealth(nextHealth);
    }

    private void BindHealth(PlayerHealthData source)
    {
        if (ReferenceEquals(health, source)) return;
        if (health != null)
        {
            health.HealthChanged -= RefreshHealth;
            health.DeathConfirmed -= OnDeathConfirmed;
        }
        health = source;
        showingDeathRecovery = false;
        if (health != null)
        {
            health.HealthChanged += RefreshHealth;
            health.DeathConfirmed += OnDeathConfirmed;
        }
        ResetHealthFeedback(health);
        RefreshHealth(health);
    }

    private void OnDeathConfirmed()
    {
        RefreshHealth(health);
    }

    // 기절 해제 판정에 쓰이는 타이머를 그대로 표시한다.
    private bool TryRefreshDeathRecovery(PlayerHealthData source)
    {
        if (source == null || !source.IsRecoveringFromDeath || !source.IsRagdoll) return false;
        showingDeathRecovery = true;
        previousHealthFill = source.NormalizedHealth;
        healthTrailHoldRemaining = 0f;
        healthTrailElapsed = healthTrailDuration;
        SetHealthTrail(0f);
        float progress = healthPlayer != null && ReferenceEquals(healthPlayer.Health, source) &&
            healthPlayer.ActionController != null ? healthPlayer.ActionController.RagdollRecoveryProgress : 0f;
        if (healthSlider != null) healthSlider.SetValueWithoutNotify(progress);
        if (healthText != null)
            healthText.text = "기절 회복 " + Mathf.FloorToInt(progress * 100f)
                .ToString(CultureInfo.InvariantCulture) + "%";
        return true;
    }

    private void TickHealthRecovery()
    {
        if (TryRefreshDeathRecovery(health) || !showingDeathRecovery) return;
        showingDeathRecovery = false;
        ResetHealthFeedback(health);
        RefreshHealth(health);
    }

    private void RefreshHealth(PlayerHealthData source)
    {
        if (TryRefreshDeathRecovery(source)) return;
        showingDeathRecovery = false;
        float next = source != null ? source.NormalizedHealth : 0f;
        if (next < previousHealthFill)
        {
            healthTrailStart = Mathf.Max(previousHealthFill,
                healthDamageTrail != null ? healthDamageTrail.anchorMax.x : previousHealthFill);
            SetHealthTrail(healthTrailStart);
            healthTrailHoldRemaining = healthTrailDelay;
            healthTrailElapsed = 0f;
        }
        else if (source == null || next > previousHealthFill)
        {
            ResetHealthFeedback(source);
        }
        previousHealthFill = next;
        if (healthSlider != null) healthSlider.SetValueWithoutNotify(next);
        if (healthText != null)
            healthText.text = source == null ? "-- / --"
                : Mathf.CeilToInt(source.CurHealth).ToString(CultureInfo.InvariantCulture) + " / " +
                  Mathf.CeilToInt(source.MaxHealth).ToString(CultureInfo.InvariantCulture);
    }

    private void ResetHealthFeedback(PlayerHealthData source)
    {
        previousHealthFill = source != null ? source.NormalizedHealth : 0f;
        healthTrailStart = previousHealthFill;
        healthTrailHoldRemaining = 0f;
        healthTrailElapsed = healthTrailDuration;
        SetHealthTrail(previousHealthFill);
    }

    private void TickHealthFeedback(float deltaTime)
    {
        if (showingDeathRecovery || health == null || healthDamageTrail == null ||
            (healthTrailHoldRemaining <= 0f && healthTrailElapsed >= healthTrailDuration)) return;
        float remaining = Mathf.Max(0f, deltaTime);
        if (healthTrailHoldRemaining > 0f)
        {
            float wait = Mathf.Min(healthTrailHoldRemaining, remaining);
            healthTrailHoldRemaining -= wait;
            remaining -= wait;
        }
        if (healthTrailHoldRemaining > 0f) return;
        healthTrailElapsed += remaining;
        float progress = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01(healthTrailElapsed / Mathf.Max(.01f, healthTrailDuration)));
        SetHealthTrail(Mathf.Lerp(healthTrailStart, health.NormalizedHealth, progress));
    }

    private void SetHealthTrail(float fill)
    {
        if (healthDamageTrail != null)
            healthDamageTrail.anchorMax = new Vector2(Mathf.Clamp01(fill), 1f);
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


