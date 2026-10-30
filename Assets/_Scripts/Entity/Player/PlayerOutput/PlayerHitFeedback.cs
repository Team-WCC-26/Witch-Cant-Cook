using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerBrain))]
public sealed class PlayerHitFeedback : MonoBehaviour
{
    [Header("피격 이펙트")]
    [InspectorName("이펙트 프리팹"), SerializeField] private ParticleSystem hitEffectPrefab;
    [InspectorName("생성 기준"), SerializeField] private Transform hitEffectAnchor;
    [InspectorName("생성 위치 보정"), SerializeField] private Vector3 hitEffectOffset = new Vector3(0f, 1.15f, 0.4f);
    [InspectorName("크기 배율"), SerializeField, Min(0f)] private float hitEffectScale = 1f;

    [Header("화면 테두리")]
    [SerializeField] private CanvasGroup overlayPrefab;
    [SerializeField] private Color hitColor = new Color(0.86f, 0.22f, 0.10f, 1f);
    [InspectorName("최대 불투명도"), SerializeField, Range(0f, 1f)] private float peakOpacity = 0.28f;
    [InspectorName("피격 효과 시간"), SerializeField, Min(0.01f)] private float duration = 0.28f;
    [InspectorName("나타나는 시간"), SerializeField, Min(0f)] private float attackTime = 0.025f;

    private PlayerBrain brain;
    private PlayerHitWobble wobble;
    private PlayerHealthData health;
    private CanvasGroup overlay;
    private Canvas overlayCanvas;
    private Image edge;
    private float previousHealth;
    private float elapsed;
    private bool localPlayer, flashing;
    public bool IsFlashing => flashing;
    public float OverlayAlpha => overlay != null ? overlay.alpha : 0f;

    private void Awake()
    {
        brain = GetComponent<PlayerBrain>();
        wobble = GetComponent<PlayerHitWobble>();
    }

    private void OnEnable() => TryBindHealth();

    private void Update()
    {
        TryBindHealth();
        if (StageReturnFlow.IsInputBlocked) ClearScreen();
        else TickVisual(Time.unscaledDeltaTime);
    }

    private void TryBindHealth()
    {
        if (brain == null) brain = GetComponent<PlayerBrain>();
        if (brain != null && health != brain.Health)
            BindHealth(brain.Health, brain.StateResolver is LocalPlayerStateResolver);
    }

    public void BindHealth(PlayerHealthData source, bool isLocalPlayer)
    {
        if (health != null) health.HealthChanged -= OnHealthChanged;
        health = source;
        localPlayer = isLocalPlayer;
        previousHealth = source != null ? source.CurHealth : 0f;
        ClearScreen();
        if (health != null) health.HealthChanged += OnHealthChanged;
    }

    private void OnHealthChanged(PlayerHealthData data)
    {
        float next = data.CurHealth;
        bool damaged = next < previousHealth;
        previousHealth = next;
        if (!damaged)
        {
            if (data.NormalizedHealth >= 1f) ClearScreen();
            return;
        }
        if (wobble == null) wobble = GetComponent<PlayerHitWobble>();
        wobble?.PlayDamage();
        PlayHitParticles();
        if (!localPlayer || StageReturnFlow.IsInputBlocked || !EnsureOverlay()) return;
        elapsed = 0f;
        flashing = true;
        overlayCanvas.enabled = true;
        // Immediate, bounded feedback; repeated hits restart instead of stacking opacity.
        overlay.alpha = peakOpacity * 0.35f;
    }

    private void PlayHitParticles()
    {
        if (hitEffectPrefab == null || hitEffectScale <= 0f) return;
        if (brain == null) brain = GetComponent<PlayerBrain>();
        Transform anchor = hitEffectAnchor != null ? hitEffectAnchor
            : brain != null && brain.FacingRoot != null ? brain.FacingRoot : transform;
        ParticleSystem effect = Instantiate(hitEffectPrefab,
            anchor.TransformPoint(hitEffectOffset), Quaternion.identity);
        effect.transform.localScale = hitEffectPrefab.transform.localScale * hitEffectScale;
        // Keep the burst in the player's scene so unloading the scene also removes it.
        if (effect.gameObject.scene != gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(effect.gameObject, gameObject.scene);
        effect.Play(true);
    }

    private bool EnsureOverlay()
    {
        if (overlay != null) return true;
        if (overlayPrefab == null) return false;
        overlay = Instantiate(overlayPrefab, transform, false);
        overlay.name = "PlayerHitOverlay";
        overlayCanvas = overlay.GetComponent<Canvas>();
        edge = overlay.GetComponentInChildren<Image>(true);
        overlay.blocksRaycasts = false;
        overlay.interactable = false;
        if (edge != null)
        {
            edge.color = hitColor;
            edge.raycastTarget = false;
        }
        overlay.alpha = 0f;
        return overlayCanvas != null;
    }

    public void TickVisual(float deltaTime)
    {
        if (!flashing || overlay == null) return;
        elapsed += Mathf.Max(0f, deltaTime);
        float total = Mathf.Max(0.01f, duration);
        if (elapsed >= total) { ClearScreen(); return; }
        float attack = Mathf.Clamp(attackTime, 0f, total * 0.5f);
        float strength = attack > 0f && elapsed < attack
            ? Mathf.Lerp(0.35f, 1f, elapsed / attack)
            : Mathf.Pow(1f - Mathf.Clamp01((elapsed - attack) / Mathf.Max(0.001f, total - attack)), 1.6f);
        overlay.alpha = Mathf.Clamp01(peakOpacity) * strength;
    }

    private void ClearScreen()
    {
        flashing = false;
        elapsed = 0f;
        if (overlay != null) overlay.alpha = 0f;
        if (overlayCanvas != null) overlayCanvas.enabled = false;
    }

    private void OnDisable()
    {
        if (health != null) health.HealthChanged -= OnHealthChanged;
        health = null;
        ClearScreen();
    }
}
