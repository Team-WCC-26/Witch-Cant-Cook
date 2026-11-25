using UnityEngine;

[DisallowMultipleComponent]
public sealed class TrashCanAnimationController : MonoBehaviour
{
    [Header("애니메이션")]
    [SerializeField] private Animator visualAnimator;
    [SerializeField] private AnimationClip intakeClip;
    [SerializeField] private AnimationClip ejectClip;

    [Header("재생 속도")]
    [SerializeField, Min(0.1f)] private float intakeAnimationSpeed = 1f;
    [SerializeField, Min(0.1f)] private float ejectAnimationSpeed = 1f;

    [Header("배출 시점")]
    [SerializeField, Min(0f), Tooltip("아래로 내려간 뒤 실제 물체를 내보내는 클립상의 시점(초)입니다.")]
    private float ejectLaunchTime = 0.22f;

    public float EjectionLaunchDelay => ejectClip != null
        ? Mathf.Clamp(ejectLaunchTime, 0f, ejectClip.length) / Mathf.Max(0.1f, ejectAnimationSpeed)
        : 0f;

    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int SwallowHash = Animator.StringToHash("Swallow");
    private static readonly int EjectBounceHash = Animator.StringToHash("EjectBounce");

    private float ejectVisualUntil;
    private bool hasRestPose;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private Vector3 restScale;

    private void OnEnable() => ResetVisual();
    private void OnDisable() => ResetVisual();

    public void PlayIntake()
    {
        // 새 투입이 진행 중인 배출 연출을 끊지 않게 한다.
        if (intakeClip == null || Time.time < ejectVisualUntil) return;
        Play(SwallowHash, intakeAnimationSpeed);
    }

    public float PlayEjection()
    {
        if (ejectClip == null || !Play(EjectBounceHash, ejectAnimationSpeed)) return 0f;
        float duration = ejectClip.length / Mathf.Max(0.1f, ejectAnimationSpeed);
        ejectVisualUntil = Time.time + duration;
        return duration;
    }

    public void ResetVisual()
    {
        ejectVisualUntil = 0f;
        if (visualAnimator == null) return;
        CaptureRestPose();
        if (visualAnimator.isActiveAndEnabled && visualAnimator.runtimeAnimatorController != null)
        {
            visualAnimator.speed = 1f;
            visualAnimator.Play(IdleHash, 0, 0f);
            visualAnimator.Update(0f);
        }
        visualAnimator.transform.SetLocalPositionAndRotation(restPosition, restRotation);
        visualAnimator.transform.localScale = restScale;
    }

    private bool Play(int stateHash, float speed)
    {
        if (!isActiveAndEnabled || visualAnimator == null || !visualAnimator.isActiveAndEnabled ||
            visualAnimator.runtimeAnimatorController == null) return false;
        CaptureRestPose();
        visualAnimator.speed = Mathf.Max(0.1f, speed);
        visualAnimator.Play(stateHash, 0, 0f);
        return true;
    }

    private void CaptureRestPose()
    {
        if (hasRestPose) return;
        restPosition = visualAnimator.transform.localPosition;
        restRotation = visualAnimator.transform.localRotation;
        restScale = visualAnimator.transform.localScale;
        hasRestPose = true;
    }
}
