using UnityEngine;
using UnityEngine.UI;

public class PlayerHPCanvas : MonoBehaviour
{
    [SerializeField] private GameObject hpBackground;
    [SerializeField] private Image hpForeground;
    [SerializeField] private PlayerBrain brain;
    [Header("피해 잔상")]
    [SerializeField] private Image hpDamageTrail;
    [InspectorName("잔상 유지 시간"), SerializeField, Min(0f)] private float trailDelay = 0.25f;
    [InspectorName("잔상 감소 시간"), SerializeField, Min(0.01f)] private float trailDuration = 0.45f;

    private PlayerHealthData health;
    private float previousFill, trailStart, holdRemaining, trailElapsed;
    public float TrailFill => hpDamageTrail != null ? hpDamageTrail.fillAmount : previousFill;

    private void Update()
    {
        BindHealthIfNeeded();
        TickFeedback(Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        if (health != null) health.HealthChanged -= Refresh;
        health = null;
        holdRemaining = 0f;
    }

    private void BindHealthIfNeeded()
    {
        if (brain != null && health != brain.Health) BindHealth(brain.Health);
    }

    public void BindHealth(PlayerHealthData source)
    {
        if (health != null) health.HealthChanged -= Refresh;
        health = source;
        holdRemaining = 0f;
        trailElapsed = trailDuration;
        if (health == null)
        {
            if (hpBackground != null) hpBackground.SetActive(false);
            return;
        }
        previousFill = health.NormalizedHealth;
        trailStart = previousFill;
        if (hpForeground != null) hpForeground.fillAmount = previousFill;
        if (hpDamageTrail != null) hpDamageTrail.fillAmount = previousFill;
        if (hpBackground != null) hpBackground.SetActive(previousFill < 1f);
        health.HealthChanged += Refresh;
    }

    private void Refresh(PlayerHealthData data)
    {
        float next = data.NormalizedHealth;
        if (next < previousFill)
        {
            trailStart = Mathf.Max(TrailFill, previousFill);
            if (hpDamageTrail != null) hpDamageTrail.fillAmount = trailStart;
            holdRemaining = trailDelay;
            trailElapsed = 0f;
        }
        else
        {
            // Healing and respawn must not look like a second damage event.
            trailStart = next;
            if (hpDamageTrail != null) hpDamageTrail.fillAmount = next;
            holdRemaining = 0f;
            trailElapsed = trailDuration;
        }
        previousFill = next;
        if (hpForeground != null) hpForeground.fillAmount = next;
        if (hpBackground != null) hpBackground.SetActive(next < 1f);
    }

    public void TickFeedback(float deltaTime)
    {
        if (health == null || hpDamageTrail == null) return;
        float remaining = Mathf.Max(0f, deltaTime);
        if (holdRemaining > 0f)
        {
            float wait = Mathf.Min(holdRemaining, remaining);
            holdRemaining -= wait;
            remaining -= wait;
        }
        if (holdRemaining > 0f) return;
        trailElapsed += remaining;
        float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(trailElapsed / Mathf.Max(0.01f, trailDuration)));
        hpDamageTrail.fillAmount = Mathf.Lerp(trailStart, health.NormalizedHealth, progress);
    }
}
