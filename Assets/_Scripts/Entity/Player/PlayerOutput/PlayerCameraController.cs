using System.Collections.Generic;
using Unity.Cinemachine;
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

    [Header("Trash Can Camera")]
    [SerializeField, Min(0.01f)] private float distanceDamping = 0.3f;
    private CinemachineThirdPersonFollow cameraFollow;
    private float normalCameraDistance;
    private float distanceVelocity;
    private bool normalObstacleAvoidance;
    private float trashCanBlend;
    private float trashCanBlendVelocity;
    private Vector3 trashCanTargetPos;
    private float trashCanPitch;
    private readonly Dictionary<TrashCanInteraction, float> trashCanDistances = new();

    private float yawDeg = 0f;
    private float pitchDeg = 0f;
    public Quaternion YawRotation => Quaternion.Euler(0f, yawDeg, 0f);
    public Vector3 AimDirection => (YawRotation * Quaternion.Euler(pitchDeg, 0f, 0f) * Vector3.forward).normalized;

    private Vector3 itemPointLocalPosition;
    private Quaternion itemPointLocalRotation = Quaternion.identity;

    private Vector3 throwPointLocalPosition;
    private Quaternion throwPointLocalRotation = Quaternion.identity;

    private bool canControlCursor = false;
    private int suppressLookThroughFrame;
    private Vector3 yawRootLocalPos;

    #region Unity Callbacks
    private void Awake()
    {
        if (brain == null && !TryGetComponent(out brain))
        {
            Debug.LogWarning("PlayerBrain is not assigned.");
            return;
        }

        input = brain.Input;

        yawDeg = NormalizeAngle(brain.transform.eulerAngles.y);

        if (brain.ItemPoint != null)
        {
            itemPointLocalPosition = brain.transform.InverseTransformPoint(
                brain.ItemPoint.position
            );
            itemPointLocalRotation = Quaternion.Inverse(brain.transform.rotation)
                * brain.ItemPoint.rotation;
        }

        if (brain.ThrowPoint != null)
        {
            Transform throwPoint = brain.ThrowPoint.transform;
            throwPointLocalPosition = brain.transform.InverseTransformPoint(
                throwPoint.position
            );
            throwPointLocalRotation = Quaternion.Inverse(brain.transform.rotation)
                * throwPoint.rotation;
        }

        if (yawRoot == null)
        {
            Debug.LogWarning("yawRoot is not assigned.");
        }
        else
        {
            yawRootLocalPos = yawRoot.localPosition;
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

        RefreshCursor(Application.isFocused);
    }

    private void OnDisable()
    {
        ResetCameraView();
        if (!canControlCursor)
        {
            return;
        }

        ApplyCursor(false);
    }

    private void Update()
    {
        UpdateCameraView();
        if (input == null || brain == null || yawRoot == null || pitchRoot == null)
        {
            return;
        }
        RefreshCursor(Application.isFocused);
        Vector2 look = Application.isFocused && Time.frameCount > suppressLookThroughFrame
            && !LobbyHudUI.IsInputBlocked && !StageReturnFlow.IsInputBlocked
            && Cursor.lockState == CursorLockMode.Locked
            && !Cursor.visible ? input.RawLookDelta : Vector2.zero;

        if (look.sqrMagnitude >= 0.000001f)
        {
            float yawDelta = look.x * cameraSensitivity;
            float pitchDelta = look.y * cameraSensitivity * (invertY ? 1f : -1f);

            yawDeg += yawDelta;
            pitchDeg = Mathf.Clamp(pitchDeg + pitchDelta, minPitch, maxPitch);
        }

        Quaternion yawRotation = YawRotation;

        // Follow the displayed body without moving its physics ancestors.
        Vector3 followPos = brain.VisualPos + brain.transform.TransformVector(yawRootLocalPos);
        yawRoot.position = Vector3.Lerp(followPos, trashCanTargetPos, trashCanBlend);
        yawRoot.rotation = yawRotation;
        pitchRoot.localRotation = Quaternion.Euler(Mathf.LerpAngle(pitchDeg, trashCanPitch, trashCanBlend), 0f, 0f);

        if (brain.Animator != null && brain.Animator.enabled)
        {
            brain.Animator.transform.rotation = yawRotation;
        }

        if (brain.ItemPoint != null)
        {
            brain.ItemPoint.SetPositionAndRotation(
                brain.transform.position + yawRotation * itemPointLocalPosition,
                yawRotation * itemPointLocalRotation
            );
        }

        if (brain.ThrowPoint != null)
        {
            brain.ThrowPoint.transform.SetPositionAndRotation(
                brain.transform.position + yawRotation * throwPointLocalPosition,
                yawRotation * throwPointLocalRotation
            );
        }
    }

    private void LateUpdate()
    {
        if (canControlCursor) RefreshCursor(Application.isFocused);
    }

    private void OnApplicationFocus(bool focus)
    {
        // Discard focus and cursor-warp deltas before resuming camera input.
        suppressLookThroughFrame = Time.frameCount + 1;
        if (canControlCursor)
        {
            RefreshCursor(focus);
        }
    }
    #endregion

    public void BindCamera(CinemachineCamera virtualCamera)
    {
        ResetCameraView();
        cameraFollow = virtualCamera != null ? virtualCamera.GetComponent<CinemachineThirdPersonFollow>() : null;
        if (cameraFollow != null)
        {
            normalCameraDistance = cameraFollow.CameraDistance;
            normalObstacleAvoidance = cameraFollow.AvoidObstacles.Enabled;
        }
    }

    public void SetTrashCanView(TrashCanInteraction source, float offset)
    {
        if (offset > 0f) trashCanDistances[source] = offset;
        else trashCanDistances.Remove(source);
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
        RefreshCursor(Application.isFocused);
    }

    public void ResetForRespawn(Quaternion rot)
    {
        ResetCameraView();
        yawDeg = NormalizeAngle(rot.eulerAngles.y);
        pitchDeg = 0f;
        if (yawRoot != null)
            yawRoot.SetPositionAndRotation(brain.VisualPos + brain.transform.TransformVector(yawRootLocalPos), YawRotation);
        if (pitchRoot != null) pitchRoot.localRotation = Quaternion.identity;
        if (canControlCursor) RefreshCursor(Application.isFocused);
    }

    private void UpdateCameraView()
    {
        if (cameraFollow == null || !canControlCursor) return;
        if (trashCanDistances.Count == 0 && trashCanBlend == 0f) return;
        float offset = 0f;
        TrashCanInteraction activeTrashCan = null;
        foreach (var entry in trashCanDistances)
        {
            if (entry.Key == null || !entry.Key.isActiveAndEnabled || entry.Value <= offset) continue;
            offset = entry.Value;
            activeTrashCan = entry.Key;
        }
        if (activeTrashCan != null)
        {
            trashCanTargetPos = activeTrashCan.CameraTargetPos;
            trashCanPitch = activeTrashCan.CameraPitch;
        }
        trashCanBlend = Mathf.SmoothDamp(trashCanBlend, activeTrashCan != null ? 1f : 0f,
            ref trashCanBlendVelocity, distanceDamping, Mathf.Infinity, Time.deltaTime);
        // Keep container walls from pulling the overhead camera back inside.
        cameraFollow.AvoidObstacles.Enabled = normalObstacleAvoidance
            && activeTrashCan == null && trashCanBlend < 0.001f;
        cameraFollow.CameraDistance = Mathf.SmoothDamp(cameraFollow.CameraDistance,
            normalCameraDistance + offset, ref distanceVelocity, distanceDamping,
            Mathf.Infinity, Time.deltaTime);
        if (activeTrashCan == null && trashCanBlend < 0.001f
            && Mathf.Abs(cameraFollow.CameraDistance - normalCameraDistance) < 0.001f)
            ResetCameraView();
    }

    private void ResetCameraView()
    {
        if (cameraFollow != null)
        {
            cameraFollow.CameraDistance = normalCameraDistance;
            cameraFollow.AvoidObstacles.Enabled = normalObstacleAvoidance;
        }
        distanceVelocity = 0f;
        trashCanBlend = 0f;
        trashCanBlendVelocity = 0f;
        trashCanDistances.Clear();
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

    private void RefreshCursor(bool focused)
    {
        if (!canControlCursor) return;
#if UNITY_EDITOR
        focused = focused && IsEditorGameViewFocused();
#endif
        bool locked = focused && lockCursor && !StageReturnFlow.IsInputBlocked && !LobbyHudUI.IsInputBlocked;
#if UNITY_EDITOR
        if (locked) AllowEditorCursorCapture();
#endif
        if (Cursor.lockState == (locked ? CursorLockMode.Locked : CursorLockMode.None)
            && Cursor.visible == !locked) return;
        suppressLookThroughFrame = Time.frameCount + 1;
        ApplyCursor(locked);
    }

    private static void ApplyCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

#if UNITY_EDITOR
    #region Editor
    private static readonly System.Type editorGameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
    private static readonly System.Reflection.MethodInfo allowEditorCursorCapture = editorGameViewType?.GetMethod(
        "AllowCursorLockAndHide", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    private static UnityEditor.EditorWindow cursorGameView;
    private static System.Action<bool> setEditorCursorCapture;

    private static bool IsEditorGameViewFocused()
    {
        UnityEditor.EditorWindow focusedWindow = UnityEditor.EditorWindow.focusedWindow;
        return focusedWindow != null && focusedWindow.GetType() == editorGameViewType && !UnityEditor.EditorApplication.isPaused;
    }

    private static void AllowEditorCursorCapture()
    {
        UnityEditor.EditorWindow focusedWindow = UnityEditor.EditorWindow.focusedWindow;
        if (focusedWindow != cursorGameView || setEditorCursorCapture == null)
        {
            cursorGameView = focusedWindow;
            setEditorCursorCapture = allowEditorCursorCapture == null ? null :
                (System.Action<bool>)System.Delegate.CreateDelegate(typeof(System.Action<bool>), focusedWindow, allowEditorCursorCapture);
        }
        // GameView disables capture on Escape/focus loss independently of Cursor state.
        setEditorCursorCapture?.Invoke(true);
    }
    #endregion
#endif
}
