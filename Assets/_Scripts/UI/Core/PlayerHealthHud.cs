using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shared local-player HP presentation for lobby and stage HUDs.
[DisallowMultipleComponent]
public sealed class PlayerHealthHud : MonoBehaviour
{
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

    private void OnEnable()
    {
        BindLocalHealthIfNeeded();
        RefreshHealth(health);
    }

    private void OnDisable()
    {
        BindHealth(null);
        healthPlayer = null;
    }

    private void Update()
    {
        BindLocalHealthIfNeeded();
        TickHealthRecovery();
        TickHealthFeedback(Time.unscaledDeltaTime);
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

}
