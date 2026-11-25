using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class PlayerInkOverlay : MonoBehaviour
{
    private readonly List<(SkinnedMeshRenderer source, SkinnedMeshRenderer overlay)> renderers = new();
    private Transform head;
    private Material inkMaterial;
    private float endTime;
    private static readonly int WorldToHeadId = Shader.PropertyToID("_WorldToHead");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    public bool IsInked => endTime > Time.time;

    #region Unity Callbacks

    private void LateUpdate()
    {
        if (inkMaterial == null) return;
        if (!IsInked || head == null)
        {
            Clear();
            return;
        }

        inkMaterial.SetMatrix(WorldToHeadId, head.worldToLocalMatrix);
        inkMaterial.SetFloat(OpacityId, Mathf.Clamp01((endTime - Time.time) / 0.5f));
        foreach (var pair in renderers)
        {
            bool visible = pair.source != null && pair.source.sharedMesh != null &&
                pair.source.enabled && pair.source.gameObject.activeInHierarchy;
            pair.overlay.enabled = visible;
            if (!visible) continue;
            pair.overlay.sharedMesh = pair.source.sharedMesh;
            pair.overlay.localBounds = pair.source.localBounds;
            for (int index = 0; index < pair.source.sharedMesh.blendShapeCount; index++)
                pair.overlay.SetBlendShapeWeight(index, pair.source.GetBlendShapeWeight(index));
        }
    }

    private void OnDisable()
    {
        Clear();
    }

    private void OnDestroy()
    {
        foreach (var pair in renderers)
            if (pair.overlay != null) Destroy(pair.overlay.gameObject);
        if (inkMaterial != null) Destroy(inkMaterial);
    }

    #endregion

    public void Apply(float duration)
    {
        if (duration <= 0f) return;
        if (inkMaterial == null && !CreateOverlay()) return;
        endTime = Mathf.Max(endTime, Time.time + duration);
        LateUpdate();
    }

    public void Clear()
    {
        endTime = 0f;
        foreach (var pair in renderers)
            if (pair.overlay != null) pair.overlay.enabled = false;
    }

    private bool CreateOverlay()
    {
        PlayerBrain brain = GetComponent<PlayerBrain>();
        if (brain == null || brain.Animator == null) return false;
        foreach (Transform bone in brain.Animator.GetComponentsInChildren<Transform>(true))
            if (bone.name == "Head") { head = bone; break; }
        if (head == null) return false;

        Material template = Resources.Load<Material>("VFX/PlayerInkOverlay");
        if (template == null)
        {
            Debug.LogError("Player ink overlay material is missing.", this);
            return false;
        }
        inkMaterial = new Material(template);

        foreach (SkinnedMeshRenderer source in brain.Animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string partName = source.name.Split('_')[0];
            if (partName != "Body" && partName != "Full") continue;
            var overlayObj = new GameObject("InkOverlay");
            overlayObj.layer = source.gameObject.layer;
            overlayObj.transform.SetParent(source.transform, false);
            var overlay = overlayObj.AddComponent<SkinnedMeshRenderer>();
            overlay.sharedMesh = source.sharedMesh;
            overlay.bones = source.bones;
            overlay.rootBone = source.rootBone;
            overlay.localBounds = source.localBounds;
            overlay.quality = source.quality;
            overlay.updateWhenOffscreen = source.updateWhenOffscreen;
            overlay.shadowCastingMode = ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            var materials = new Material[source.sharedMaterials.Length];
            for (int index = 0; index < materials.Length; index++) materials[index] = inkMaterial;
            overlay.sharedMaterials = materials;
            overlay.enabled = false;
            renderers.Add((source, overlay));
        }
        return true;
    }
}
