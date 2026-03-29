using UnityEngine;

public sealed class PlayerCameraController : MonoBehaviour
{
    [Header("Brain")]
    [SerializeField] private PlayerBrain brain = null;
    private PlayerInputHandler input = null;

    [Header("Refs")]
    [SerializeField] private Transform yawRoot = null;
    [SerializeField] private Transform pitchRoot = null;

    [Header("Cursor Setting")]
    [SerializeField] private bool lockCursor = true;

    [Header("Tune Setting")]
    [SerializeField] private float cameraSensitivity = 0.12f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float minPitch = -70f;
    [SerializeField] private float maxPitch = 70f;

    // Camera Rotation State
    private float yawDeg = 0f;
    private float pitchDeg = 0f;

    private void Awake()
    {
        if (brain == null && !TryGetComponent(out brain))
        {
            Debug.LogWarning("PlayerBrain is not assigned.");
            return;
        }

        input = brain.Input;

        if (yawRoot == null)
            Debug.LogWarning("yawRoot is not assigned.");
        else yawDeg = NormalizeAngle(yawRoot.localEulerAngles.y);

        if (pitchRoot == null)
            Debug.LogWarning("pitchRoot is not assigned.");
        else
            pitchDeg = NormalizeAngle(pitchRoot.localEulerAngles.x);
    }

    private void OnEnable()
    {
        ApplyCursor(lockCursor);
    }

    private void OnDisable()
    {
        ApplyCursor(false);
    }

    private void Update()
    {
        if (input == null || yawRoot == null || pitchRoot == null) return;

        Vector2 look = input.LookDelta;
        if (look.sqrMagnitude < 0.000001f) return;

        float yawDelta = look.x * cameraSensitivity;
        float pitchDelta = look.y * cameraSensitivity * (invertY ? 1f : -1f);

        yawDeg += yawDelta;
        yawRoot.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

        pitchDeg = Mathf.Clamp(pitchDeg + pitchDelta, minPitch, maxPitch);
        pitchRoot.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
    }

    private static float NormalizeAngle(float euler)
    {
        float value = euler;
        if (value > 180f) value -= 360f;
        return value;
    }

    private static void ApplyCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}