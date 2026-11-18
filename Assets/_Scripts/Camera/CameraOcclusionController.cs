using Unity.Cinemachine;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraOcclusionController : MonoBehaviour
{
    [SerializeField] private CinemachineCamera gameplayCamera;
    [SerializeField] private float occludedAlpha = 0.15f;
    [SerializeField] private float fadeOutDuration = 0.1f;
    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private float restoreDelay = 0.1f;
    [SerializeField] private float detectionPadding = 0.1f;
    [SerializeField] private int minOccludedSamples = 2;
    [SerializeField] private float segmentLengthOffset;
    [SerializeField] private bool showDebugSegments = true;

    private static readonly ProfilerMarker updateMarker = new("CameraOcclusion.Update");
    private readonly Vector3[] samplePoints = new Vector3[5];
    private readonly Transform[] sampleBones = new Transform[5];
    private Camera outputCamera;
    private CinemachineBrain cameraBrain;
    private PlayerBrain localPlayer;
    private int lastRenderFrame = -1;
    private bool hasFadedTargets;
    private Vector3 lastCameraPos;
    private bool hasDebugSegments;

    #region Unity Callbacks

    private void Awake()
    {
        outputCamera = GetComponent<Camera>();
        cameraBrain = GetComponent<CinemachineBrain>();
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        lastRenderFrame = -1;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RestoreTargets();
        hasDebugSegments = false;
    }

    private void LateUpdate()
    {
        // Disabled gameplay cameras do not receive rendering callbacks.
        if (!CanFade())
        {
            RestoreTargets();
            hasDebugSegments = false;
        }
    }

    private void OnValidate()
    {
        occludedAlpha = Mathf.Clamp01(occludedAlpha);
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
        fadeInDuration = Mathf.Max(0f, fadeInDuration);
        restoreDelay = Mathf.Max(0f, restoreDelay);
        detectionPadding = Mathf.Max(0f, detectionPadding);
        minOccludedSamples = Mathf.Clamp(minOccludedSamples, 1, samplePoints.Length);
    }

    private void OnDrawGizmos()
    {
        if (!showDebugSegments || !hasDebugSegments || !Application.isPlaying) return;
        Color previousColor = Gizmos.color;
        Gizmos.color = Color.yellow;
        foreach (Vector3 point in samplePoints) Gizmos.DrawLine(lastCameraPos, point);
        Gizmos.color = previousColor;
    }

    #endregion

    /// <summary>Binds the local character and its gameplay camera.</summary>
    public void BindPlayer(PlayerBrain player, CinemachineCamera virtualCamera)
    {
        RestoreTargets();
        localPlayer = player;
        gameplayCamera = virtualCamera;
        lastRenderFrame = -1;
        hasDebugSegments = false;
        for (int index = 0; index < sampleBones.Length; index++) sampleBones[index] = null;

        if (virtualCamera != null && virtualCamera.TryGetComponent(out CinemachineThirdPersonFollow follow))
            follow.AvoidObstacles.Enabled = false;

        Animator animator = player != null ? player.Animator : null;
        if (animator == null || !animator.isHuman) return;
        sampleBones[0] = animator.GetBoneTransform(HumanBodyBones.Head);
        sampleBones[1] = animator.GetBoneTransform(HumanBodyBones.Chest);
        sampleBones[2] = animator.GetBoneTransform(HumanBodyBones.Hips);
        sampleBones[3] = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        sampleBones[4] = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
    }

    private bool CanFade()
    {
        if (outputCamera == null || !outputCamera.isActiveAndEnabled || localPlayer == null
            || !localPlayer.isActiveAndEnabled || gameplayCamera == null || cameraBrain == null
            || !cameraBrain.isActiveAndEnabled || StageReturnFlow.IsSettlementActive)
            return false;

        if (localPlayer.CameraController == null || localPlayer.CameraController.IsSpecialViewActive)
            return false;

        // Pot cameras and all special-camera blends keep their original rendering.
        return cameraBrain.ActiveVirtualCamera == gameplayCamera && !cameraBrain.IsBlending;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
    {
        if (renderingCamera != outputCamera || lastRenderFrame == Time.frameCount) return;
        lastRenderFrame = Time.frameCount;
        if (!CanFade())
        {
            RestoreTargets();
            hasDebugSegments = false;
            return;
        }

        using (updateMarker.Auto())
        {
            Vector3 cameraPos = outputCamera.transform.position;
            UpdateSamplePoints(cameraPos);
            lastCameraPos = cameraPos;
            hasDebugSegments = true;
            var targets = CameraOcclusionTarget.ActiveTargets;
            for (int index = 0; index < targets.Count; index++)
            {
                CameraOcclusionTarget target = targets[index];
                bool useOverride = target.OverrideSettings;
                float padding = useOverride ? target.DetectionPadding : detectionPadding;
                bool occluded = target.IsOccluding(cameraPos, samplePoints, padding, minOccludedSamples);
                target.UpdateFade(occluded,
                    useOverride ? target.OccludedAlpha : occludedAlpha,
                    useOverride ? target.FadeOutDuration : fadeOutDuration,
                    useOverride ? target.FadeInDuration : fadeInDuration,
                    useOverride ? target.RestoreDelay : restoreDelay,
                    Time.unscaledDeltaTime);
            }
            hasFadedTargets = true;
        }
    }

    private void UpdateSamplePoints(Vector3 cameraPos)
    {
        Bounds bodyBounds = localPlayer.Body != null ? localPlayer.Body.Bounds
            : new Bounds(localPlayer.VisualPos + Vector3.up * 0.5f, Vector3.one);
        Vector3 center = bodyBounds.center;
        float height = Mathf.Max(0.2f, bodyBounds.size.y);
        Vector3 side = outputCamera.transform.right
            * Mathf.Max(0.1f, Mathf.Min(bodyBounds.extents.x, bodyBounds.extents.z) * 0.7f);

        samplePoints[0] = center + Vector3.up * (height * 0.4f);
        samplePoints[1] = center + Vector3.up * (height * 0.15f);
        samplePoints[2] = center - Vector3.up * (height * 0.2f);
        samplePoints[3] = samplePoints[1] - side;
        samplePoints[4] = samplePoints[1] + side;
        for (int index = 0; index < samplePoints.Length; index++)
        {
            if (sampleBones[index] != null) samplePoints[index] = sampleBones[index].position;
            if (segmentLengthOffset == 0f) continue;
            Vector3 direction = samplePoints[index] - cameraPos;
            float length = direction.magnitude;
            if (length > 0.000001f)
                samplePoints[index] = cameraPos + direction * (Mathf.Max(0f, length + segmentLengthOffset) / length);
        }
    }

    private void RestoreTargets()
    {
        if (!hasFadedTargets) return;
        var targets = CameraOcclusionTarget.ActiveTargets;
        for (int index = 0; index < targets.Count; index++) targets[index].RestoreImmediate();
        hasFadedTargets = false;
    }
}
