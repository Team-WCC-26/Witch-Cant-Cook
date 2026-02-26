using UnityEngine;

public sealed class PlayerCameraController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerBrain brain = null;
    [SerializeField] private Transform yawRoot = null;     // 플레이어 루트(Yaw)
    [SerializeField] private Transform pitchPivot = null;  // 카메라 피벗(Pitch)

    [Header("Tuning")]
    [SerializeField] private float sensitivity = 0.12f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float minPitchDeg = -70f;
    [SerializeField] private float maxPitchDeg = 70f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursor = true;

    private PlayerInputHandler input;

    private float pitchDeg = 0f;

    private void Awake()
    {
        if (brain == null) brain = GetComponent<PlayerBrain>();
        input = brain.Input;

        if (yawRoot == null) yawRoot = transform.root;
        if (pitchPivot == null) pitchPivot = transform;

        pitchDeg = NormalizePitch(pitchPivot.localEulerAngles.x);
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
        if (input == null || yawRoot == null || pitchPivot == null) return;

        Vector2 look = input.LookDelta;
        if (look.sqrMagnitude < 0.000001f) return;

        float yawDelta = look.x * sensitivity;
        float pitchDelta = look.y * sensitivity * (invertY ? 1f : -1f);

        yawRoot.Rotate(0f, yawDelta, 0f, Space.Self);

        pitchDeg = Mathf.Clamp(pitchDeg + pitchDelta, minPitchDeg, maxPitchDeg);
        pitchPivot.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
    }

    private static float NormalizePitch(float eulerX)
    {
        float x = eulerX;
        if (x > 180f) x -= 360f;
        return x;
    }

    private static void ApplyCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}