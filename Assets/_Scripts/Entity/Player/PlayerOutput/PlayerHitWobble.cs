using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerBrain))]
public sealed class PlayerHitWobble : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField, Min(0f)] private float angle = 12f;
    [SerializeField, Min(0.01f)] private float duration = 0.35f;

    private PlayerBrain brain;
    private Quaternion restRotation;
    private float elapsed;
    private float direction = 1f;
    private bool playing;

    private void Awake()
    {
        brain = GetComponent<PlayerBrain>();
        if (visualRoot == null) visualRoot = brain.FacingRoot;
        if (visualRoot != null) restRotation = visualRoot.localRotation;
    }

    public void PlayHit(Vector3 attackerPosition)
    {
        if (visualRoot == null || angle <= 0f || brain.Animator == null || !brain.Animator.enabled)
            return;

        Vector3 toAttacker = attackerPosition - transform.position;
        float side = Vector3.Dot(transform.right, toAttacker.normalized);
        direction = Mathf.Abs(side) > 0.15f ? Mathf.Sign(side) : -direction;
        elapsed = 0f;
        playing = true;
        visualRoot.localRotation = restRotation;
    }

    private void LateUpdate()
    {
        if (!playing) return;
        if (brain.Animator == null || !brain.Animator.enabled)
        {
            StopHit();
            return;
        }

        elapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(elapsed / duration);
        float tilt = Mathf.Sin(progress * Mathf.PI * 3f) * (1f - progress) * angle * direction;
        visualRoot.localRotation = restRotation * Quaternion.Euler(tilt * 0.25f, 0f, tilt);
        if (progress >= 1f) StopHit();
    }

    private void OnDisable()
    {
        StopHit();
    }

    private void StopHit()
    {
        playing = false;
        if (visualRoot != null) visualRoot.localRotation = restRotation;
    }
}
