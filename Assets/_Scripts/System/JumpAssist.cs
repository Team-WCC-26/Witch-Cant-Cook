using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public class JumpAssist : MonoBehaviour
{
    [Header("Board Motion")]
    [Tooltip("Local Z rotation. Positive angles lift a board placed along local +X.")]
    [SerializeField, Range(-180f, 180f)] private float _liftAngle = 60f;
    [SerializeField, Min(0.01f)] private float _liftDuration = 0.12f;
    [SerializeField] private Ease _liftEase = Ease.OutCubic;
    [SerializeField, Min(0f)] private float _holdDuration = 0.1f;
    [SerializeField, Min(0.01f)] private float _returnDuration = 0.8f;
    [SerializeField] private Ease _returnEase = Ease.InOutSine;

    [Header("Motion Test")]
    [SerializeField] private bool _playOnStart;

    private Quaternion _restRotation;
    private Sequence _motion;

    private void Awake()
    {
        _restRotation = transform.localRotation;
    }

    private void Start()
    {
        if (_playOnStart) PlayMotion();
    }

    // Call from the component menu during Play Mode, or from a future trigger.
    [ContextMenu("Play Motion")]
    public void PlayMotion()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        if (_motion != null && _motion.IsActive()) return;

        transform.localRotation = _restRotation;
        _motion = CreateMotion(_restRotation);
        _motion.OnComplete(() =>
        {
            transform.localRotation = _restRotation;
            _motion = null;
        });
    }

    private Sequence CreateMotion(Quaternion restRotation)
    {
        Quaternion liftedRotation = restRotation * Quaternion.Euler(0f, 0f, _liftAngle);
        Sequence motion = DOTween.Sequence();
        motion.Append(transform.DOLocalRotateQuaternion(liftedRotation, Mathf.Max(0.01f, _liftDuration))
            .SetEase(_liftEase));
        motion.AppendInterval(Mathf.Max(0f, _holdDuration));
        motion.Append(transform.DOLocalRotateQuaternion(restRotation, Mathf.Max(0.01f, _returnDuration))
            .SetEase(_returnEase));
        return motion;
    }

#if UNITY_EDITOR
    public Sequence CreatePreviewMotion()
    {
        return CreateMotion(transform.localRotation).SetAutoKill(false).Pause();
    }
#endif

    private void OnDisable()
    {
        _motion?.Kill();
        _motion = null;
        transform.localRotation = _restRotation;
    }
}
