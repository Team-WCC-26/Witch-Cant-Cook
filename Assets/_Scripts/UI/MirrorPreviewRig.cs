using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Rendering.Universal;

internal sealed class MirrorPreviewRig : MonoBehaviour
{
    private Animator previewAnimator;
    private PlayerAppearanceCatalog.SlotBinding slotBinding;
    internal RenderTexture Texture { get; private set; }

    private Camera previewCam;
    private SkinnedMeshRenderer[] renderers;
    private bool needsFraming;
    private AsyncOperationHandle<GameObject> assetLifetime;

    #region Unity Callbacks
    private void LateUpdate()
    {
        if (!needsFraming) return;
        needsFraming = false;
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null) continue;
            if (hasBounds) bounds.Encapsulate(renderer.bounds);
            else bounds = renderer.bounds;
            hasBounds = true;
        }
        if (!hasBounds) return;

        previewCam.transform.position = bounds.center + Vector3.forward * (bounds.extents.z + 3f);
        previewCam.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / previewCam.aspect) * 1.23f;
        previewCam.farClipPlane = bounds.size.z + 6f;
    }

    private void OnDestroy()
    {
        if (previewCam != null) previewCam.targetTexture = null;
        if (Texture != null) Destroy(Texture);
        if (assetLifetime.IsValid()) Addressables.Release(assetLifetime);
    }
    #endregion

    internal void Initialize(GameObject characterPrefab, int layer)
    {
        assetLifetime = Addressables.LoadAssetAsync<GameObject>("CostumeMirrorUI");
        GameObject character = Instantiate(characterPrefab, transform);
        previewAnimator = character.GetComponent<Animator>();
        renderers = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer renderer in renderers)
            renderer.updateWhenOffscreen = true;
        foreach (Transform part in character.GetComponentsInChildren<Transform>(true))
            part.gameObject.layer = layer;

        const int width = 592;
        const int height = 940;
        Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "Costume Mirror Preview"
        };
        var cameraObj = new GameObject("Preview Camera");
        cameraObj.transform.SetParent(transform, false);
        cameraObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        previewCam = cameraObj.AddComponent<Camera>();
        previewCam.orthographic = true;
        previewCam.aspect = (float)width / height;
        previewCam.clearFlags = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = Color.clear;
        previewCam.cullingMask = 1 << layer;
        previewCam.allowHDR = false;
        previewCam.allowMSAA = false;
        previewCam.targetTexture = Texture;
        UniversalAdditionalCameraData cameraData = previewCam.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;

        var lightObj = new GameObject("Preview Light");
        lightObj.transform.SetParent(transform, false);
        lightObj.transform.localPosition = new Vector3(0f, 3f, 2f);
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.cullingMask = 1 << layer;
        light.range = 10f;
        light.intensity = 4f;
        Reframe();
    }

    internal void ApplySlots(string[] names, ushort[] slots)
    {
        if (previewAnimator == null) return;
        slotBinding ??= new PlayerAppearanceCatalog.SlotBinding(previewAnimator, names);
        if (slotBinding.Apply(slots)) Reframe();
    }

    internal void Reframe() => needsFraming = true;
}
