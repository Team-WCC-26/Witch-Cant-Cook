using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerBrain))]
public sealed class PlayerHitWobble : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [InspectorName("흔들림 각도"), SerializeField, Min(0f)] private float angle = 20f;
    [InspectorName("흔들림 시간"), SerializeField, Min(0.01f)] private float duration = 0.4f;
    [InspectorName("앞뒤 기울기 비율"), SerializeField, Range(0f, 1f)] private float pitchRatio = 0.4f;

    private PlayerBrain brain;
    private Quaternion restRotation = Quaternion.identity;
    private float elapsed;
    private float direction = 1f;
    private bool playing, initialized;
    private int lastHitFrame = -1;
    public bool IsPlaying => playing;

    private void Awake() => EnsureReferences();

    private bool EnsureReferences()
    {
        if (brain == null) brain = GetComponent<PlayerBrain>();
        if (visualRoot == null && brain != null) visualRoot = brain.FacingRoot;
        if (visualRoot == null) return false;
        if (!initialized)
        {
            restRotation = visualRoot.localRotation;
            initialized = true;
        }
        return true;
    }

    public void PlayHit(Vector3 attackerPosition)
    {
        if (!EnsureReferences() || angle <= 0f || brain.Animator == null || !brain.Animator.enabled) return;
        Vector3 toAttacker = attackerPosition - transform.position;
        float side = Vector3.Dot(transform.right, toAttacker.normalized);
        direction = Mathf.Abs(side) > 0.15f ? Mathf.Sign(side) : -direction;
        elapsed = 0f;
        playing = true;
        lastHitFrame = Time.frameCount;
        visualRoot.localRotation = restRotation;
    }

    public void PlayDamage()
    {
        // A rocket can request the contact wobble and lower health in the same frame.
        if (playing && lastHitFrame == Time.frameCount) return;
        PlayHit(transform.position);
    }

    private void LateUpdate() => TickVisual(Time.deltaTime);

    public void TickVisual(float deltaTime)
    {
        if (!playing) return;
        if (brain.Animator == null || !brain.Animator.enabled)
        {
            StopHit();
            return;
        }
        elapsed += Mathf.Max(0f, deltaTime);
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
        float tilt = Mathf.Sin(progress * Mathf.PI * 3f) * (1f - progress) * angle * direction;
        visualRoot.localRotation = restRotation * Quaternion.Euler(tilt * pitchRatio, 0f, tilt);
        if (progress >= 1f) StopHit();
    }

    private void OnDisable() => StopHit();

    private void StopHit()
    {
        playing = false;
        if (initialized && visualRoot != null) visualRoot.localRotation = restRotation;
    }
}
