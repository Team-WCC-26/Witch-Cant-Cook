using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000)]
public sealed class CursorController : MonoBehaviour
{
    private static CursorController instance;
    private PlayerCameraController gameplayOwner;
    private bool lockCursor;
    private bool cursorReleased;
    private bool wasFocused;
    private bool wasUIInputBlocked;
    private bool isLocked;
    private bool centerCursorPending;
    private int suppressInputThroughFrame;
    private readonly List<RaycastResult> pointerHits = new();

    public static bool IsPointerInputAllowed
    {
        get
        {
            if (instance == null) return false;
            instance.RefreshState();
            return instance.isLocked && Time.frameCount > instance.suppressInputThroughFrame
                && !(Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
        }
    }

    #region Unity Callbacks
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureInitialized()
    {
        if (instance == null) new GameObject(nameof(CursorController)).AddComponent<CursorController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update += OnEditorUpdate;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= OnEditorUpdate;
#endif
        if (instance == this) ApplyCursor(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        RefreshState();
        if (gameplayOwner == null || !IsFocused() || IsUIInputBlocked()) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            cursorReleased = true;
        else if (cursorReleased && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
            && !IsPointerOverUI())
            cursorReleased = false;
        RefreshState();
    }

    private void LateUpdate()
    {
        // Observe UI changes made during Update, including Escape closing a popup.
        RefreshState();
    }

    private void OnApplicationFocus(bool focus)
    {
        suppressInputThroughFrame = Time.frameCount + 1;
        RefreshState(focus);
    }

#if UNITY_EDITOR
    private void OnEditorUpdate()
    {
        // Game updates may stop while the Editor is unfocused or paused.
        if (IsFocused() != wasFocused) RefreshState();
    }
#endif
    #endregion

    public static void SetGameplayOwner(PlayerCameraController owner, bool shouldLock)
    {
        EnsureInitialized();
        if (instance.gameplayOwner != owner) instance.cursorReleased = false;
        instance.gameplayOwner = owner;
        instance.lockCursor = shouldLock;
        instance.RefreshState();
    }

    public static void ReleaseGameplayOwner(PlayerCameraController owner)
    {
        if (instance == null || instance.gameplayOwner != owner) return;
        instance.gameplayOwner = null;
        instance.cursorReleased = false;
        instance.RefreshState();
    }

    private static bool IsUIInputBlocked() => LobbyHudUI.IsInputBlocked || StageReturnFlow.IsInputBlocked;

    private void RefreshState(bool focus = true)
    {
        bool focused = focus && IsFocused();
        bool uiInputBlocked = IsUIInputBlocked();
        if ((focused && !wasFocused) || (!uiInputBlocked && wasUIInputBlocked))
            cursorReleased = false;
        if (focused != wasFocused || uiInputBlocked != wasUIInputBlocked)
            suppressInputThroughFrame = Time.frameCount + 1;
        wasFocused = focused;
        wasUIInputBlocked = uiInputBlocked;

        bool locked = gameplayOwner != null && gameplayOwner.isActiveAndEnabled
            && focused && lockCursor && !cursorReleased && !uiInputBlocked;
        bool stateChanged = locked != isLocked;
        bool cursorChanged = Cursor.lockState != (locked ? CursorLockMode.Locked : CursorLockMode.None)
            || Cursor.visible != !locked;
        if (!stateChanged && !cursorChanged && !(centerCursorPending && focused)) return;
        suppressInputThroughFrame = Time.frameCount + 1;
#if UNITY_EDITOR
        if (locked) AllowEditorCursorCapture();
#endif
        ApplyCursor(locked);
        isLocked = locked;
    }

    private void ApplyCursor(bool locked)
    {
        if (!locked && (isLocked || !Cursor.visible || Cursor.lockState == CursorLockMode.Locked))
            centerCursorPending = true;
        if (locked) centerCursorPending = false;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        // Defer warping until focus returns to avoid moving another application's pointer.
        if (!locked && centerCursorPending && IsFocused() && Mouse.current != null)
        {
            Mouse.current.WarpCursorPosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            centerCursorPending = false;
        }
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null || Mouse.current == null) return false;
        var pointer = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        pointerHits.Clear();
        EventSystem.current.RaycastAll(pointer, pointerHits);
        foreach (RaycastResult hit in pointerHits)
            if (hit.module is GraphicRaycaster) return true;
        return false;
    }

    private static bool IsFocused()
    {
#if UNITY_EDITOR
        return Application.isFocused && IsEditorGameViewFocused();
#else
        return Application.isFocused;
#endif
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
        return UnityEditorInternal.InternalEditorUtility.isApplicationActive && focusedWindow != null
            && focusedWindow.GetType() == editorGameViewType && !UnityEditor.EditorApplication.isPaused;
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
        // Match Player capture after the Game View releases it on Escape or focus loss.
        setEditorCursorCapture?.Invoke(true);
    }
    #endregion
#endif
}
