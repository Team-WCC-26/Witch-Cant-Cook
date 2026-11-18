using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class CameraOcclusionTarget : MonoBehaviour
{
    [Serializable]
    public struct MaterialTemplate
    {
        public Material source;
        public Material transparent;
    }

    [Serializable]
    private struct DetectionBox
    {
        public Transform space;
        public Bounds bounds;
    }

    private sealed class RendererState
    {
        public Renderer renderer;
        public Material[] originalMaterials;
        public Material[] fadedMaterials;
        public ShadowCastingMode originalShadowCasting;
        public bool changed;
        public Transform transform;
        public Bounds localBounds;
        public Bounds worldBounds;
        public Matrix4x4 localToWorld;
        public Matrix4x4 worldToLocal;
        public bool staticGeometry;
        public bool skinned;
    }

    private sealed class FadeMaterial
    {
        public Material material;
        public Color originalColor;
    }

    [SerializeField] private Renderer[] targetRenderers = Array.Empty<Renderer>();
    [SerializeField, HideInInspector] private MaterialTemplate[] materialTemplates = Array.Empty<MaterialTemplate>();
    [SerializeField] private DetectionBox[] detectionBoxes = Array.Empty<DetectionBox>();
    [SerializeField] private bool overrideSettings;
    [SerializeField] private float occludedAlpha = 0.15f;
    [SerializeField] private float fadeOutDuration = 0.1f;
    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private float restoreDelay = 0.1f;
    [SerializeField] private float detectionPadding = 0.1f;

    private static readonly List<CameraOcclusionTarget> activeTargets = new();
    private static readonly int baseColorId = Shader.PropertyToID("_BaseColor");
    private readonly List<FadeMaterial> fadeMaterials = new();
    private RendererState[] rendererStates;
    private float currentAlpha = 1f;
    private float clearTime;
    private float fadeRange = 1f;
    private bool materialsPrepared;
    private bool faded;
    private bool[] occludedSamples = Array.Empty<bool>();

#if WCC_TEST_TOOLS
    private int preparedRendererCount;
    public int FadedRendererCount => faded ? preparedRendererCount : 0;
    public static double PreparationMilliseconds { get; private set; }
    public static int CreatedMaterialCount { get; private set; }
#endif

    public static IReadOnlyList<CameraOcclusionTarget> ActiveTargets => activeTargets;
    public bool OverrideSettings => overrideSettings;
    public float OccludedAlpha => Mathf.Clamp01(occludedAlpha);
    public float FadeOutDuration => Mathf.Max(0f, fadeOutDuration);
    public float FadeInDuration => Mathf.Max(0f, fadeInDuration);
    public float RestoreDelay => Mathf.Max(0f, restoreDelay);
    public float DetectionPadding => Mathf.Max(0f, detectionPadding);

    #region Unity Callbacks

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        CacheRenderers();
        if (!activeTargets.Contains(this)) activeTargets.Add(this);
    }

    private void OnDisable()
    {
        activeTargets.Remove(this);
        RestoreImmediate();
    }

    private void OnDestroy()
    {
        activeTargets.Remove(this);
        RestoreImmediate();
        foreach (FadeMaterial entry in fadeMaterials)
            if (entry.material != null) Destroy(entry.material);
        fadeMaterials.Clear();
    }

    private void OnValidate()
    {
        occludedAlpha = Mathf.Clamp01(occludedAlpha);
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
        fadeInDuration = Mathf.Max(0f, fadeInDuration);
        restoreDelay = Mathf.Max(0f, restoreDelay);
        detectionPadding = Mathf.Max(0f, detectionPadding);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        if (detectionBoxes != null && detectionBoxes.Length > 0)
        {
            foreach (DetectionBox box in detectionBoxes)
            {
                Transform boxSpace = box.space != null ? box.space : transform;
                Gizmos.matrix = boxSpace.localToWorldMatrix;
                Gizmos.DrawWireCube(box.bounds.center, box.bounds.size);
            }
        }
        else if (targetRenderers != null)
        {
            foreach (Renderer targetRenderer in targetRenderers)
            {
                if (targetRenderer == null) continue;
                Gizmos.matrix = targetRenderer.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(targetRenderer.localBounds.center, targetRenderer.localBounds.size);
            }
        }
        Gizmos.matrix = previousMatrix;
    }

    #endregion

#if WCC_TEST_TOOLS
    /// <summary>Clears per-frame material preparation measurements.</summary>
    public static void ResetPerformanceCounters()
    {
        PreparationMilliseconds = 0;
        CreatedMaterialCount = 0;
    }
#endif

    /// <summary>Checks whether enough distinct sight segments intersect this target.</summary>
    public bool IsOccluding(Vector3 cameraPos, Vector3[] samplePoints, float padding, int minOccludedSamples = 1)
    {
        if (!isActiveAndEnabled || samplePoints == null || samplePoints.Length == 0 || targetRenderers == null)
            return false;
        CacheRenderers();
        if (occludedSamples.Length != samplePoints.Length) occludedSamples = new bool[samplePoints.Length];
        else Array.Clear(occludedSamples, 0, occludedSamples.Length);
        int requiredCount = Mathf.Clamp(minOccludedSamples, 1, samplePoints.Length);
        int hitCount = 0;
        padding = Mathf.Max(0f, padding);
        if (detectionBoxes != null && detectionBoxes.Length > 0)
        {
            bool hasVisibleRenderer = false;
            foreach (RendererState state in rendererStates)
            {
                if (state.renderer == null || !state.renderer.enabled || !state.renderer.gameObject.activeInHierarchy)
                    continue;
                hasVisibleRenderer = true;
                break;
            }
            if (!hasVisibleRenderer) return false;
            foreach (DetectionBox box in detectionBoxes)
            {
                Transform boxSpace = box.space != null ? box.space : transform;
                if (!boxSpace.gameObject.activeInHierarchy) continue;
                hitCount = CountBoxHits(boxSpace.worldToLocalMatrix, box.bounds, cameraPos,
                    samplePoints, padding, occludedSamples, hitCount, requiredCount);
                if (hitCount >= requiredCount) return true;
            }
            return false;
        }

        foreach (RendererState state in rendererStates)
        {
            Renderer targetRenderer = state.renderer;
            if (targetRenderer == null || !targetRenderer.enabled || !targetRenderer.gameObject.activeInHierarchy)
                continue;
            UpdateGeometry(state);
            Bounds worldBounds = state.worldBounds;
            worldBounds.Expand(padding * 2f);
            bool candidate = false;
            for (int index = 0; index < samplePoints.Length; index++)
            {
                if (occludedSamples[index] || !IntersectsSegment(worldBounds, cameraPos, samplePoints[index]))
                    continue;
                candidate = true;
                break;
            }
            if (!candidate) continue;
            hitCount = CountBoxHits(state.worldToLocal, state.localBounds, cameraPos,
                samplePoints, padding, occludedSamples, hitCount, requiredCount);
            if (hitCount >= requiredCount) return true;
        }
        return false;
    }

    /// <summary>Updates the fade and restores original materials after the clear delay.</summary>
    public void UpdateFade(bool occluded, float alpha, float outDuration, float inDuration,
        float delay, float deltaTime)
    {
        deltaTime = Mathf.Max(0f, deltaTime);
        alpha = Mathf.Clamp01(alpha);
        if (occluded && alpha < 1f) fadeRange = 1f - alpha;
        if (occluded) clearTime = 0f;
        else clearTime += deltaTime;
        float targetAlpha = occluded || clearTime < Mathf.Max(0f, delay)
            ? Mathf.Clamp01(alpha) : 1f;
        if (!occluded && !faded) return;
        float duration = targetAlpha < currentAlpha ? outDuration : inDuration;
        float previousAlpha = currentAlpha;
        currentAlpha = duration <= 0f ? targetAlpha
            : Mathf.MoveTowards(currentAlpha, targetAlpha, deltaTime * fadeRange / duration);
        if (currentAlpha >= 1f)
        {
            RestoreImmediate();
            return;
        }
        if (faded && currentAlpha == previousAlpha) return;
        if (!faded) ApplyFadeMaterials();
        foreach (FadeMaterial entry in fadeMaterials)
        {
            Color color = entry.originalColor;
            color.a *= currentAlpha;
            entry.material.SetColor(baseColorId, color);
        }
    }

    /// <summary>Restores original renderer materials and shadow casting immediately.</summary>
    public void RestoreImmediate()
    {
        if (faded && rendererStates != null)
        {
            foreach (RendererState state in rendererStates)
            {
                if (!state.changed || state.renderer == null) continue;
                state.renderer.sharedMaterials = state.originalMaterials;
                state.renderer.shadowCastingMode = state.originalShadowCasting;
            }
        }
        faded = false;
        currentAlpha = 1f;
        clearTime = 0f;
        fadeRange = 1f;
    }

    private void CacheRenderers()
    {
        if (rendererStates != null) return;
        if (targetRenderers == null) targetRenderers = Array.Empty<Renderer>();
        rendererStates = new RendererState[targetRenderers.Length];
        for (int index = 0; index < targetRenderers.Length; index++)
        {
            Renderer targetRenderer = targetRenderers[index];
            rendererStates[index] = new RendererState
            {
                renderer = targetRenderer,
                originalMaterials = targetRenderer != null ? targetRenderer.sharedMaterials : Array.Empty<Material>(),
                originalShadowCasting = targetRenderer != null ? targetRenderer.shadowCastingMode : ShadowCastingMode.Off,
                transform = targetRenderer != null ? targetRenderer.transform : null,
                localBounds = targetRenderer != null ? targetRenderer.localBounds : default,
                worldBounds = targetRenderer != null ? targetRenderer.bounds : default,
                localToWorld = targetRenderer != null ? targetRenderer.transform.localToWorldMatrix : default,
                worldToLocal = targetRenderer != null ? targetRenderer.transform.worldToLocalMatrix : default,
                staticGeometry = targetRenderer != null && targetRenderer.gameObject.isStatic,
                skinned = targetRenderer is SkinnedMeshRenderer
            };
        }
    }

    private static void UpdateGeometry(RendererState state)
    {
        Matrix4x4 localToWorld = state.transform.localToWorldMatrix;
        // Static flags may remain on animated furniture imported from older scenes.
        if (state.staticGeometry && !state.skinned && localToWorld == state.localToWorld) return;
        state.localToWorld = localToWorld;
        state.worldToLocal = state.transform.worldToLocalMatrix;
        state.worldBounds = state.renderer.bounds;
        if (state.skinned) state.localBounds = state.renderer.localBounds;
    }

    private void ApplyFadeMaterials()
    {
        CacheRenderers();
        if (!materialsPrepared) PrepareMaterials();
        foreach (RendererState state in rendererStates)
        {
            if (!state.changed || state.renderer == null) continue;
            state.originalShadowCasting = state.renderer.shadowCastingMode;
            state.renderer.sharedMaterials = state.fadedMaterials;
            state.renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        faded = true;
    }

    private void PrepareMaterials()
    {
#if WCC_TEST_TOOLS
        long preparationStart = System.Diagnostics.Stopwatch.GetTimestamp();
        preparedRendererCount = 0;
#endif
        var materialCopies = new Dictionary<Material, Material>();
        bool unsupported = false;
        foreach (RendererState state in rendererStates)
        {
            state.fadedMaterials = (Material[])state.originalMaterials.Clone();
            for (int index = 0; index < state.originalMaterials.Length; index++)
            {
                Material original = state.originalMaterials[index];
                if (original == null) continue;
                if (original.renderQueue >= (int)RenderQueue.Transparent ||
                    (original.HasProperty("_Surface") && original.GetFloat("_Surface") > 0.5f)) continue;
                if (original.shader.name != "Universal Render Pipeline/Lit")
                {
                    unsupported = true;
                    continue;
                }
                if (!materialCopies.TryGetValue(original, out Material copy))
                {
                    copy = CreateFadeMaterial(original);
                    materialCopies.Add(original, copy);
                    fadeMaterials.Add(new FadeMaterial { material = copy, originalColor = original.GetColor(baseColorId) });
                }
                state.fadedMaterials[index] = copy;
                state.changed = true;
            }
#if WCC_TEST_TOOLS
            if (state.changed) preparedRendererCount++;
#endif
        }
        if (unsupported)
            Debug.LogWarning("[Camera Occlusion] Unsupported opaque shaders were left unchanged on " + name, this);
        materialsPrepared = true;
#if WCC_TEST_TOOLS
        PreparationMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - preparationStart)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
#endif
    }

    private Material CreateFadeMaterial(Material original)
    {
        Material template = original;
        if (materialTemplates != null)
        {
            foreach (MaterialTemplate entry in materialTemplates)
            {
                if (entry.source != original || entry.transparent == null) continue;
                template = entry.transparent;
                break;
            }
        }
        var material = new Material(template)
        {
            name = original.name + " (Camera Occlusion)",
            hideFlags = HideFlags.DontSave
        };
        material.CopyPropertiesFromMaterial(original);
        ConfigureTransparentMaterial(material);
#if WCC_TEST_TOOLS
        CreatedMaterialCount++;
#endif
        return material;
    }

    /// <summary>Configures the URP Lit alpha blend state for a fade material.</summary>
    public static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null || material.shader.name != "Universal Render Pipeline/Lit") return;
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_BlendModePreserveSpecular", 0f);
        material.SetFloat("_AlphaToMask", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("DepthNormals", false);
        material.SetShaderPassEnabled("MotionVectors", false);
    }

    private static int CountBoxHits(Matrix4x4 worldToLocal, Bounds bounds, Vector3 cameraPos,
        Vector3[] samplePoints, float padding, bool[] hits, int hitCount, int requiredCount)
    {
        Vector3 localPadding = new(
            new Vector3(worldToLocal.m00, worldToLocal.m01, worldToLocal.m02).magnitude * padding,
            new Vector3(worldToLocal.m10, worldToLocal.m11, worldToLocal.m12).magnitude * padding,
            new Vector3(worldToLocal.m20, worldToLocal.m21, worldToLocal.m22).magnitude * padding);
        bounds.Expand(localPadding * 2f);
        Vector3 localCamera = worldToLocal.MultiplyPoint3x4(cameraPos);
        for (int index = 0; index < samplePoints.Length; index++)
        {
            if (hits[index] || !IntersectsSegment(bounds, localCamera,
                    worldToLocal.MultiplyPoint3x4(samplePoints[index]))) continue;
            hits[index] = true;
            hitCount++;
            if (hitCount >= requiredCount) break;
        }
        return hitCount;
    }

    private static bool IntersectsSegment(Bounds bounds, Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float entry = 0f;
        float exit = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(direction[axis]) < 0.000001f)
            {
                if (start[axis] < min[axis] || start[axis] > max[axis]) return false;
                continue;
            }
            float near = (min[axis] - start[axis]) / direction[axis];
            float far = (max[axis] - start[axis]) / direction[axis];
            if (near > far) (near, far) = (far, near);
            entry = Mathf.Max(entry, near);
            exit = Mathf.Min(exit, far);
            if (entry > exit) return false;
        }
        return true;
    }

    #region Editor

#if UNITY_EDITOR
    /// <summary>Collects enabled mesh renderers owned by this target.</summary>
    [ContextMenu("Configure Renderers")]
    public void ConfigureRenderers()
    {
        if (Application.isPlaying) return;
        UnityEditor.Undo.RecordObject(this, "Configure Camera Occlusion Target");
        var renderers = new List<Renderer>();
        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
        {
            if (!candidate.enabled || !(candidate is MeshRenderer || candidate is SkinnedMeshRenderer)) continue;
            if (candidate.GetComponentInParent<CameraOcclusionTarget>(true) != this) continue;
            if (candidate.GetComponent<TMPro.TMP_Text>() != null) continue;
            renderers.Add(candidate);
            UnityEditor.StaticEditorFlags flags = UnityEditor.GameObjectUtility.GetStaticEditorFlags(candidate.gameObject);
            UnityEditor.StaticEditorFlags updatedFlags = flags &
                ~(UnityEditor.StaticEditorFlags.OccluderStatic | UnityEditor.StaticEditorFlags.BatchingStatic);
            if (flags != updatedFlags)
            {
                UnityEditor.Undo.RecordObject(candidate.gameObject, "Prepare Camera Occlusion Renderer");
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(candidate.gameObject, updatedFlags);
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(candidate.gameObject);
            }
        }
        targetRenderers = renderers.ToArray();
        rendererStates = null;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    #endregion
}
