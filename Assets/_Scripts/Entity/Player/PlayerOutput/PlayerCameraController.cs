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

    private float yawDeg = 0f;
    private float pitchDeg = 0f;

    private bool canControlCursor = false;

    private void Awake()
    {
        if (brain == null && !TryGetComponent(out brain))
        {
            Debug.LogWarning("PlayerBrain is not assigned.");
            return;
        }

        input = brain.Input;

        yawDeg = NormalizeAngle(brain.transform.eulerAngles.y);

        if (yawRoot == null)
        {
            Debug.LogWarning("yawRoot is not assigned.");
        }
        else
        {
            yawRoot.localRotation = Quaternion.identity;
        }

        if (pitchRoot == null)
        {
            Debug.LogWarning("pitchRoot is not assigned.");
        }
        else
        {
            pitchDeg = NormalizeAngle(pitchRoot.localEulerAngles.x);
        }
    }

    private void OnEnable()
    {
        if (!canControlCursor)
        {
            return;
        }

        ApplyCursor(lockCursor);
    }

    private void OnDisable()
    {
        if (!canControlCursor)
        {
            return;
        }

        ApplyCursor(false);
    }

    public void SetLocalControlActive(bool isActive)
    {
        if (!isActive)
        {
            if (canControlCursor)
            {
                ApplyCursor(false);
            }

            canControlCursor = false;
            enabled = false;
            return;
        }

        canControlCursor = true;
        enabled = true;
        ApplyCursor(lockCursor);
    }

    private void Update()
    {
        if (input == null || brain == null || yawRoot == null || pitchRoot == null)
        {
            return;
        }

        Vector2 look = input.RawLookDelta;

        if (look.sqrMagnitude >= 0.000001f)
        {
            float yawDelta = look.x * cameraSensitivity;
            float pitchDelta = look.y * cameraSensitivity * (invertY ? 1f : -1f);

            yawDeg += yawDelta;
            pitchDeg = Mathf.Clamp(pitchDeg + pitchDelta, minPitch, maxPitch);
        }

        brain.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
        yawRoot.localRotation = Quaternion.identity;
        pitchRoot.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
    }

    private void OnApplicationFocus(bool focus)
    {
        if (canControlCursor)
        {
            ApplyCursor(focus && lockCursor);
        }
    }

    private static float NormalizeAngle(float euler)
    {
        float value = euler;
        if (value > 180f)
        {
            value -= 360f;
        }

        return value;
    }

    private static void ApplyCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
