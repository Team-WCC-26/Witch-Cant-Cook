using UnityEngine;
using UnityEngine.Rendering.Universal;

// A fixed, independent camera for the Recipe_Big settlement board.
// The player's camera rig keeps its normal position, orientation and Cinemachine bindings.
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class StageSettlementCamera : MonoBehaviour
{
    [SerializeField] private Camera settlementCamera;
    [Tooltip("로컬 플레이어의 카메라를 찾지 못했을 때 사용할 카메라입니다.")]
    [SerializeField] private Camera gameplayCamera;

    [Header("Scroll Framing")]
    [SerializeField] private RectTransform scrollCanvas;
    [Tooltip("결산 진입 시 두루마리 전체가 보이도록 전용 카메라의 구도를 맞춥니다. 끄면 배치한 Transform을 그대로 사용합니다.")]
    [SerializeField] private bool frameScrollOnEntry = true;
    [SerializeField, Min(1f)] private float frameMargin = 1.15f;
    private readonly Vector3[] scrollCorners = new Vector3[4];

    public Camera RenderingCamera => settlementCamera != null ? settlementCamera : GetComponent<Camera>();

    private Camera previousGameplayCamera;
    private bool previousGameplayEnabled;
    private bool showingSettlement;
    private GameObject hiddenPointer;

    // Multiplayer Play Mode can start another client from an older scene snapshot.
    // Every client's visible settlement board must have its own dedicated camera.
    public static StageSettlementCamera EnsureForScroll(RectTransform canvas)
    {
        if (canvas == null) return null;
        Transform board = canvas;
        while (board != null && board.name != "Recipe_Big") board = board.parent;
        if (board == null) return null;

        StageSettlementCamera existing = board.GetComponentInChildren<StageSettlementCamera>(true);
        if (existing != null)
        {
            if (existing.scrollCanvas == null) existing.scrollCanvas = canvas;
            return existing;
        }

        GameObject cameraObject = new("SettlementUICamera");
        cameraObject.SetActive(false);
        cameraObject.transform.SetParent(board, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        StageSettlementCamera controller = cameraObject.AddComponent<StageSettlementCamera>();
        controller.settlementCamera = camera;
        controller.scrollCanvas = canvas;
        Camera source = controller.ResolveGameplayCamera();
        if (source != null)
        {
            camera.CopyFrom(source);
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderType = CameraRenderType.Base;
            if (source.TryGetComponent(out UniversalAdditionalCameraData sourceData))
            {
                cameraData.renderPostProcessing = sourceData.renderPostProcessing;
                cameraData.antialiasing = sourceData.antialiasing;
                cameraData.antialiasingQuality = sourceData.antialiasingQuality;
                cameraData.volumeLayerMask = sourceData.volumeLayerMask;
            }
        }
        camera.enabled = false;
        cameraObject.SetActive(true);
        Debug.Log("[Settlement Camera] Created missing camera for this client's Recipe_Big.", controller);
        return controller;
    }

    private void Awake()
    {
        if (settlementCamera == null) settlementCamera = GetComponent<Camera>();
        settlementCamera.enabled = false;
    }

    private void LateUpdate()
    {
        if (!StageReturnFlow.IsSettlementActive)
        {
            RestoreGameplayCamera();
            return;
        }
        if (showingSettlement) return;

        Camera source = ResolveGameplayCamera();
        if (source == null || source == settlementCamera || settlementCamera == null) return;
        // Additional players can render to a different display, viewport or RenderTexture.
        settlementCamera.targetDisplay = source.targetDisplay;
        settlementCamera.targetTexture = source.targetTexture;
        settlementCamera.rect = source.rect;
        settlementCamera.aspect = source.aspect;
        if (frameScrollOnEntry) FrameScroll();
        previousGameplayCamera = source;
        previousGameplayEnabled = source.enabled;
        // Find only an active pointer so an already hidden one stays hidden on return.
        hiddenPointer = GameObject.Find("Pointer");
        if (hiddenPointer != null) hiddenPointer.SetActive(false);
        source.enabled = false;
        settlementCamera.enabled = true;
        showingSettlement = true;
    }

    [ContextMenu("Frame Scroll")]
    private void FrameScroll()
    {
        if (settlementCamera == null || scrollCanvas == null) return;
        scrollCanvas.GetWorldCorners(scrollCorners);
        Vector3 up = scrollCorners[1] - scrollCorners[0];
        Vector3 right = scrollCorners[3] - scrollCorners[0];
        if (up.sqrMagnitude < 0.000001f || right.sqrMagnitude < 0.000001f) return;

        float aspect = Mathf.Max(0.01f, settlementCamera.aspect);
        float halfHeight = Mathf.Max(up.magnitude * 0.5f, right.magnitude * 0.5f / aspect) * Mathf.Max(1f, frameMargin);
        float halfFov = Mathf.Clamp(settlementCamera.fieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad;
        float distance = Mathf.Max(halfHeight / Mathf.Tan(halfFov), settlementCamera.nearClipPlane * 2f);
        if (settlementCamera.orthographic) settlementCamera.orthographicSize = halfHeight;
        Vector3 forward = Vector3.Cross(right, up).normalized;
        Vector3 center = (scrollCorners[0] + scrollCorners[2]) * 0.5f;
        settlementCamera.transform.SetPositionAndRotation(center - forward * distance, Quaternion.LookRotation(forward, up));
    }

    private Camera ResolveGameplayCamera()
    {
        PlayerSpawnManager players = PlayerSpawnManager.Instance;
        if (players != null && players.TryGetPlayer(players.MyID, out PlayerBrain player) &&
            player != null && player.PlayerCam != null && player.PlayerCam != settlementCamera)
            return player.PlayerCam;
        if (gameplayCamera != null && gameplayCamera != settlementCamera) return gameplayCamera;
        Camera main = Camera.main;
        return main != settlementCamera ? main : null;
    }

    private void OnDisable() => RestoreGameplayCamera();

    private void RestoreGameplayCamera()
    {
        if (settlementCamera != null) settlementCamera.enabled = false;
        if (showingSettlement && previousGameplayCamera != null)
            previousGameplayCamera.enabled = previousGameplayEnabled;
        if (hiddenPointer != null) hiddenPointer.SetActive(true);
        hiddenPointer = null;
        previousGameplayCamera = null;
        showingSettlement = false;
    }
}
