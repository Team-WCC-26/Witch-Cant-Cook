using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

[Flags]
public enum IngredientAction : byte
{
    None = 0,

    Cut = 1,
    Grill = 2,
    Boil = 4,
    Cook = 8
}

[Serializable]
public class IngredientActionVisual
{
    [SerializeField] private IngredientAction action;
    [SerializeField] private Mesh mesh;
    [SerializeField] private Material[] materials;

    public IngredientAction Action => action;
    public Mesh Mesh => mesh;
    public Material[] Materials => materials;
}

public class IngredientReaction : MonoBehaviour, IPoolable
{
    public event Action<IngredientAction> OnActionCompleted;

    [SerializeField] private CatchableObj catchable;
    public CatchableObj Catchable => catchable;

    [Header("Components")]
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private Renderer targetRenderer;
    [Tooltip("비워 두면 MeshFilter와 같은 오브젝트의 MeshCollider를 사용합니다. 상태별 시각 메시와 함께 갱신합니다.")]
    [SerializeField] private MeshCollider meshCollider;

    [Header("Interact Gauge")]
    [SerializeField] private InteractionGaugeUI gaugeUI;
    [Header("충돌 파티클")]
    [UnityEngine.Serialization.FormerlySerializedAs("wallImpactScale")]
    [SerializeField, Min(0f), InspectorName("충돌 파티클 크기 배율")] private float impactScale = 1f;
    [Tooltip("이 값 이상의 충격량(N·s)일 때 재생합니다. 일반 충돌은 Collision.impulse, 직접 계산하는 튕김은 질량 × 속도 변화량을 사용합니다.")]
    [SerializeField, Min(0f), InspectorName("최소 충격량")] private float impactMinImpulse = 0.5f;
    [UnityEngine.Serialization.FormerlySerializedAs("wallImpactCooldown")]
    [SerializeField, Min(0f), InspectorName("충돌 파티클 재생 간격")] private float impactCooldown = 0.1f;
    private float lastImpactTime = float.NegativeInfinity;

    private IngredientAction lastAction = IngredientAction.None;
    private IngredientAction completedActions = IngredientAction.None;
    private int curHP;
    private int maxHP;
    private float lastCutProcess;
    private Coroutine wobbleRoutine;
    private Transform wobblePivot;
    private Transform motionPivot;
    private bool conveyorVisualActive, hitWobbleActive, presentationSourceWasEnabled;
    private Vector3 conveyorWorldOffset;
    private Quaternion conveyorVisualRotation = Quaternion.identity;
    private readonly List<Material> presentationMaterials = new();
    private readonly List<Material> proxyMaterials = new();
    private MeshFilter wobbleMeshFilter;
    private MeshRenderer wobbleMeshRenderer;
    private MaterialPropertyBlock wobblePropertyBlock;
    private float wobbleDirection = 1f;

    [Header("Cut Hit Wobble")]
    [SerializeField, Min(0f)] private float wobbleAngle = 12f;
    [SerializeField, Min(0.01f)] private float wobbleDuration = 0.35f;
    public float CurGuage => 1 - (float)curHP / maxHP;
    public InteractionGaugeUI GaugeUI => gaugeUI;

    [Header("Placement")]
    [SerializeField] private TransformData panPlacement;
    [SerializeField] private TransformData platePlacement;
    [SerializeField, HideInInspector] private Vector3 plateOffsetPos = Vector3.zero;
    [SerializeField, HideInInspector] private Vector3 plateOffsetEuler = Vector3.zero;
    public Vector3 PlateOffsetPos => plateOffsetPos;
    public Vector3 PlateOffsetEuler => plateOffsetEuler;

    private Vector3 originalScale;
    private bool hasOriginalScale;
    private bool hasOriginalState;
    private Mesh originalMesh;
    private Mesh originalColliderMesh;
    private BoxCollider boxCollider;
    private Vector3 originalBoxCenter, originalBoxSize;
    private Material[] originalMaterials;
    private bool originalRendererEnabled;
    private readonly List<MonoBehaviour> traitComponents = new();
    private readonly List<bool> originalTraitEnabled = new();
    private readonly List<Collider> bounceMaterialColliders = new();
    private readonly List<PhysicsMaterial> originalBounceMaterials = new();

    [Header("Action Visuals")]
    [SerializeField] private IngredientActionVisual[] actionVisuals;

    [Header("Grill Overlay")]
    [SerializeField] private Color grillColor = new Color(55f / 255f, 22f / 255f, 6f / 255f, 1f);
    [SerializeField] private Vector3 grillEuler = new Vector3(0f, 0f, 25f);
    [SerializeField, Min(1f)] private float grillStripeCount = 5f;
    [SerializeField, Range(0.01f, 0.9f)] private float grillStripeWidth = 0.34f;
    [SerializeField, Range(0f, 1f)] private float grillStripeOffset;
    private MeshFilter grillMeshFilter;
    private MeshRenderer grillRenderer;
    private Material grillMaterial;
    private bool grillPreview;

    #region Unity Callbacks

    private void Awake()
    {
        if (catchable == null) catchable = GetComponent<CatchableObj>();
        CacheOriginalState();
        CacheOriginalScale();
        InitializeGauge();
    }


    private void OnDisable()
    {
        ResetImpact();
        ClearConveyorVisualMotion();
        StopHitWobble();
        grillPreview = false;
        if (grillRenderer != null) grillRenderer.gameObject.SetActive(false);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Scripted bounce motion reports its own sweep contact below.
        if (TryGetComponent(out IngredientTraitBounce bounce) && bounce.IsSimulating) return;
        if (collision.contactCount == 0) return;
        ContactPoint contact = collision.GetContact(0);
        float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
        for (int i = 1; i < collision.contactCount; i++)
        {
            ContactPoint candidate = collision.GetContact(i);
            float candidateSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, candidate.normal));
            if (candidateSpeed <= speed) continue;
            contact = candidate;
            speed = candidateSpeed;
        }
        PlayImpact(collision.collider, contact.point, contact.normal, collision.impulse.magnitude);
    }

    private void ResetImpact()
    {
        lastImpactTime = float.NegativeInfinity;
    }

    public void PlayImpact(Collider other, Vector3 point, Vector3 normal, float impulse)
    {
        if (!isActiveAndEnabled || catchable == null ||
            catchable.IsHold || catchable.ParentEntityId > 0 ||
            catchable.IsSubmissionPending || catchable.IsSubmissionReturning ||
            catchable.Data is not Ingredient ingredient || other == null || other.isTrigger ||
            other.transform.IsChildOf(transform) ||
            impulse <= 0f || impulse < impactMinImpulse ||
            Time.time - lastImpactTime < impactCooldown)
            return;

        lastImpactTime = Time.time;
        SoundManager.GetOrCreate().Play(
            GetComponent<IngredientMushroom>() != null ? SoundId.MushroomImpact : SoundId.ObjectImpact, point);
        if (impactScale > 0f) IngredientCutVfx.PlayAt(point + normal * 0.01f, ingredient, impactScale);
    }

    private void LateUpdate()
    {
        UpdateGrillOverlay();
    }

    private void OnDestroy()
    {
        ReleaseGrillOverlay();
    }

    #endregion

    public bool IsActionCompleted(IngredientAction action)
    {
        return (completedActions & action) != 0;
    }

    public void ApplyPanPlacement(Transform slot)
    {
        ApplyPlacement(slot, panPlacement, Vector3.zero, Vector3.zero);
    }

    public void ApplyPlatePlacement(Transform slot)
    {
        ApplyPlacement(slot, platePlacement, plateOffsetPos, plateOffsetEuler);
        RefreshPlateScale(slot);
    }

    // A stored plate inherits the pot's scale until it is ejected. Recalculate after
    // any change to the plate's ancestry or scale, without changing placement offsets.
    public void RefreshPlateScale(Transform slot)
    {
        if (slot == null || transform.parent != slot) return;
        CacheOriginalScale();

        // Keep the prefab's size in world space, independent of the plate's scale.
        // Measure each rotated axis so placement rotation and scaled ancestors are included.
        Quaternion rotation = transform.localRotation;
        Vector3 inheritedScale = new(
            slot.TransformVector(rotation * Vector3.right).magnitude,
            slot.TransformVector(rotation * Vector3.up).magnitude,
            slot.TransformVector(rotation * Vector3.forward).magnitude);

        const float MinAxisScale = 0.000001f;
        Vector3 desiredScale = new Vector3(
            inheritedScale.x > MinAxisScale ? originalScale.x / inheritedScale.x : 0f,
            inheritedScale.y > MinAxisScale ? originalScale.y / inheritedScale.y : 0f,
            inheritedScale.z > MinAxisScale ? originalScale.z / inheritedScale.z : 0f);
        if ((transform.localScale - desiredScale).sqrMagnitude > 0.000000000001f)
            transform.localScale = desiredScale;
    }

    public void RestoreOriginalScale()
    {
        CacheOriginalScale();
        transform.localScale = originalScale;
    }

    public void ResetForPool()
    {
        ResetImpact();
        CacheOriginalState();
        RestoreOriginalScale();
        ClearConveyorVisualMotion();
        StopHitWobble();
        InitializeGauge();
        lastAction = IngredientAction.None;
        completedActions = IngredientAction.None;
        grillPreview = false;
        if (grillRenderer != null) grillRenderer.gameObject.SetActive(false);
        lastCutProcess = 0f;
        wobbleDirection = 1f;

        gaugeUI?.Hide();
        if (meshFilter != null) meshFilter.sharedMesh = originalMesh;
        ApplyColliderMesh(originalColliderMesh);
        if (boxCollider != null)
        {
            boxCollider.center = originalBoxCenter;
            boxCollider.size = originalBoxSize;
        }
        if (targetRenderer != null)
        {
            targetRenderer.sharedMaterials = originalMaterials;
            targetRenderer.enabled = originalRendererEnabled;
        }
        // Pool return deactivates the object first, so restoration cannot start effects here.
        for (int i = 0; i < traitComponents.Count; i++)
            if (traitComponents[i] != null) traitComponents[i].enabled = originalTraitEnabled[i];
        for (int i = 0; i < bounceMaterialColliders.Count; i++)
            if (bounceMaterialColliders[i] != null)
                bounceMaterialColliders[i].sharedMaterial = originalBounceMaterials[i];
    }

    private void CacheOriginalState()
    {
        if (hasOriginalState) return;
        if (meshFilter == null) meshFilter = GetComponentInChildren<MeshFilter>(true);
        if (targetRenderer == null && meshFilter != null) targetRenderer = meshFilter.GetComponent<Renderer>();
        originalMesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (meshCollider == null && meshFilter != null)
            meshCollider = meshFilter.GetComponent<MeshCollider>();
        originalColliderMesh = meshCollider != null ? meshCollider.sharedMesh : null;
        if (meshFilter != null) boxCollider = meshFilter.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            originalBoxCenter = boxCollider.center;
            originalBoxSize = boxCollider.size;
        }
        if (targetRenderer != null)
        {
            originalMaterials = targetRenderer.sharedMaterials;
            originalRendererEnabled = targetRenderer.enabled;
        }
        foreach (MonoBehaviour component in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component is not IngredientTrait && component is not IngredientTraitBounce &&
                component is not IngredientTraitTracker && component is not IngredientTraitAreaCreator) continue;
            // A nested ingredient has its own lifecycle and must retain its own state.
            if (component.GetComponentInParent<IngredientReaction>(true) != this) continue;
            traitComponents.Add(component);
            originalTraitEnabled.Add(component.enabled);
        }
        if (traitComponents.Exists(component => component is IngredientTraitBounce))
        {
            foreach (Collider col in GetComponentsInChildren<Collider>(true))
            {
                if (col.GetComponentInParent<IngredientReaction>(true) != this) continue;
                bounceMaterialColliders.Add(col);
                originalBounceMaterials.Add(col.sharedMaterial);
            }
        }
        hasOriginalState = true;
    }

    private void DisableTraitsAfterCut()
    {
        CacheOriginalState();
        foreach (MonoBehaviour component in traitComponents)
        {
            if (component == null) continue;
            component.enabled = false; // OnDisable cancels timers, flight, and subscriptions.
            component.StopAllCoroutines();
        }
        // Clear the actual colliders here, including when Bounce was already disabled.
        // A component's OnDisable alone is not an authoritative cut-state transition.
        foreach (Collider col in bounceMaterialColliders)
            if (col != null) col.sharedMaterial = null;
        if (bounceMaterialColliders.Count > 0)
        {
            int remaining = bounceMaterialColliders.FindAll(col => col != null && col.sharedMaterial != null).Count;
            Debug.Log($"[Mushroom Cut] entity={catchable?.NetworkId}, colliders={bounceMaterialColliders.Count}, materialRemaining={remaining}", this);
        }
    }

    public void ApplyCutProgress(float process)
    {
        if (IsActionCompleted(IngredientAction.Cut)) return;

        process = Mathf.Clamp01(process);
        gaugeUI?.ShowProgress(1f - process);

        if (process <= lastCutProcess + 0.0001f || process >= 1f) return;
        lastCutProcess = process;
        PlayHitWobble();
    }

    public bool Interact(IngredientAction action, int dmg = 0)
    {
        if (IsActionBlocked(action))
        {
            return false;
        }

        if (lastAction != action)
        {
            lastAction = action;
            curHP = maxHP; // Reset HP when action changes
        }
        else if (curHP <= 0)
        {
            return false;
        }

        // Apply damage and check for completion
        curHP = (int)MathF.Max(0, curHP - dmg);
        // TODO: Apply mesh and shader changes based on HP progress.
        // TODO: Show completed state flag.
        if (curHP <= 0)
        {
            switch (action)
            {
                case IngredientAction.Cut:
                    break;
                case IngredientAction.Grill:
                    break;
                case IngredientAction.Boil:
                    break;
                case IngredientAction.Cook:
                    break;
                default:
                    break;
            }

            CompleteAction(action);
            return true;
        }

        return false;
    }

    public void ApplyServerAction(IngredientAction action)
    {
        // Server visual
        lastAction = action;
        CompleteAction(action);
    }

    private void CacheOriginalScale()
    {
        if (hasOriginalScale) return;
        originalScale = transform.localScale;
        hasOriginalScale = true;
    }

    private void ApplyPlacement(Transform slot, TransformData placement,
        Vector3 defaultPosition, Vector3 defaultRotation)
    {
        CacheOriginalScale();
        transform.SetParent(slot, false);
        transform.localPosition = placement != null ? placement.PositionOffset : defaultPosition;
        transform.localRotation = Quaternion.Euler(placement != null ? placement.RotationOffset : defaultRotation);
        transform.localScale = placement != null && !placement.Scale.Equals(Vector3.zero)
            ? placement.Scale : originalScale;
    }

    private void CompleteAction(IngredientAction action)
    {
        IngredientAction newlyCompleted = action & ~completedActions;
        completedActions |= action;
        if ((newlyCompleted & IngredientAction.Cut) != 0)
        {
            SoundManager.GetOrCreate().Play(SoundId.KnifeHit, transform.position);
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            Vector3 scale = transform.localScale;
            DisableTraitsAfterCut();
            // Trait cleanup can restore an animated root to its spawn pose (e.g. corn).
            // Keep the current placement; only the cut transition below may reset rotation.
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = scale;
            StopHitWobble();
            gaugeUI?.Hide();
            PlayCutParticle();
        }
        ApplyVisual(completedActions);
        if ((newlyCompleted & IngredientAction.Cut) != 0) ResetCutPose();
        UpdateGrillOverlay();
        if (newlyCompleted != IngredientAction.None)
            OnActionCompleted?.Invoke(newlyCompleted);
    }
    
    private void ResetCutPose()
    {
        // Placement and conveyor motion own the pose while the ingredient is attached.
        if (catchable != null && (catchable.IsHold || catchable.Holder != null ||
            catchable.ParentEntityId > 0 || ConveyorBeltRegistry.IsOwnedByAnyBelt(catchable.NetworkId)))
            return;
        if (transform.parent != null && transform.parent.GetComponentInParent<CatchableObj>(true) != null)
            return;

        Rigidbody body = catchable != null ? catchable.Rb : GetComponent<Rigidbody>();
        transform.rotation = Quaternion.identity;
        if (body == null) return;
        body.rotation = Quaternion.identity;
        // Stop the old tumble from immediately overturning the newly cut mesh.
        if (!body.isKinematic) body.angularVelocity = Vector3.zero;
    }

    private bool IsActionBlocked(IngredientAction action)
    {
        if (catchable.Data is not Ingredient ingredient)
            return false;

        return ((IngredientAction)ingredient.conditionFlag & action) != 0;
    }

    #region Visual Apply
    private void ApplyVisual(IngredientAction action)
    {
        IngredientActionVisual visual = FindActionVisual(action);

        if (visual == null && (action & IngredientAction.Grill) != 0) {
            IngredientAction fallback = (action & ~IngredientAction.Grill) | IngredientAction.Cook;
            visual = FindActionVisual(fallback);
        }
        if (visual == null) return;

        ApplyMesh(visual.Mesh);
        ApplyMaterials(visual.Materials);
    }

    private IngredientActionVisual FindActionVisual(IngredientAction action)
    {
        if (actionVisuals == null) return null;

        foreach (IngredientActionVisual visual in actionVisuals)
        {
            if (visual == null) continue;
            if (visual.Action == action) return visual;
        }

        return null;
    }

    private void ApplyMesh(Mesh mesh)
    {
        if (mesh == null) return;
        if (meshFilter == null) return;

        meshFilter.sharedMesh = mesh;
        ApplyColliderMesh(mesh);
        // A cut mesh can be much flatter and have a different pivot than the whole ingredient.
        // Its collision bounds must change too, otherwise it rests above/below the visible surface.
        if (IsActionCompleted(IngredientAction.Cut) && boxCollider != null)
        {
            boxCollider.center = mesh.bounds.center;
            boxCollider.size = mesh.bounds.size;
        }
    }

    private void ApplyColliderMesh(Mesh mesh)
    {
        if (meshCollider == null || mesh == null) return;
        bool needsConvex = meshCollider.attachedRigidbody != null;
        if (meshCollider.sharedMesh == mesh && (!needsConvex || meshCollider.convex)) return;

        // Runtime cooking needs CPU mesh data. Never discard a working collider for
        // an unreadable action mesh, which would leave the ingredient falling through the map.
        if (!mesh.isReadable)
        {
            Debug.LogWarning($"[IngredientReaction] Cannot rebuild collider for '{mesh.name}': enable Read/Write on its model import settings. Keeping the previous collision mesh.", this);
            return;
        }

        // Assigning a different sharedMesh rebuilds it; clearing it first is unnecessary.
        if (needsConvex) meshCollider.convex = true;
        meshCollider.sharedMesh = mesh;
    }

    private void ApplyMaterials(Material[] materials)
    {
        if (materials == null) return;
        if (materials.Length == 0) return;
        if (targetRenderer == null) return;

        targetRenderer.sharedMaterials = materials;
    }
    #endregion

    private void UpdateGrillOverlay()
    {
        bool visible = isActiveAndEnabled && (grillPreview || IsActionCompleted(IngredientAction.Grill));
        if (!visible)
        {
            if (grillRenderer != null) grillRenderer.gameObject.SetActive(false);
            return;
        }

        if (meshFilter == null) meshFilter = GetComponentInChildren<MeshFilter>();
        if (targetRenderer == null && meshFilter != null) targetRenderer = meshFilter.GetComponent<Renderer>();
        bool wobbling = motionPivot != null && motionPivot.gameObject.activeSelf;
        MeshFilter sourceFilter = wobbling ? wobbleMeshFilter : meshFilter;
        Renderer sourceRenderer = wobbling ? wobbleMeshRenderer : targetRenderer;
        if (sourceFilter == null || sourceFilter.sharedMesh == null || sourceRenderer == null) return;

        if (grillMaterial == null)
        {
            Shader shader = Resources.Load<Shader>("VFX/IngredientGrillOverlay");
            if (shader == null) return;
            grillMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        if (grillRenderer == null)
        {
            GameObject overlay = new GameObject("GrillOverlay") { hideFlags = HideFlags.HideAndDontSave };
            grillMeshFilter = overlay.AddComponent<MeshFilter>();
            grillRenderer = overlay.AddComponent<MeshRenderer>();
            grillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            grillRenderer.receiveShadows = false;
            grillRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            grillRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        Transform overlayTransform = grillRenderer.transform;
        if (overlayTransform.parent != sourceFilter.transform)
            overlayTransform.SetParent(sourceFilter.transform, false);
        overlayTransform.localPosition = Vector3.zero;
        overlayTransform.localRotation = Quaternion.identity;
        overlayTransform.localScale = Vector3.one;
        grillRenderer.gameObject.layer = sourceRenderer.gameObject.layer;
        grillRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        grillRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        grillRenderer.sortingOrder = sourceRenderer.sortingOrder;
        Mesh mesh = sourceFilter.sharedMesh;
        if (grillMeshFilter.sharedMesh != mesh)
        {
            grillMeshFilter.sharedMesh = mesh;
            Material[] materials = new Material[mesh.subMeshCount];
            for (int index = 0; index < materials.Length; index++) materials[index] = grillMaterial;
            grillRenderer.sharedMaterials = materials;
        }

        // Normalize by one uniform size to preserve continuity across all surface faces.
        Bounds bounds = mesh.bounds;
        float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        grillMaterial.SetColor("_StripeColor", grillColor);
        grillMaterial.SetVector("_StripeDirection", Quaternion.Euler(grillEuler) * Vector3.right);
        grillMaterial.SetVector("_BoundsCenter", bounds.center);
        grillMaterial.SetFloat("_StripeScale", Mathf.Max(1f, grillStripeCount) / Mathf.Max(size, 0.0001f));
        grillMaterial.SetFloat("_StripeWidth", grillStripeWidth);
        grillMaterial.SetFloat("_StripeOffset", grillStripeOffset);
        grillRenderer.gameObject.SetActive(sourceRenderer.enabled && sourceRenderer.gameObject.activeInHierarchy);
    }

    private void ReleaseGrillOverlay()
    {
        if (grillRenderer != null)
        {
            if (Application.isPlaying) Destroy(grillRenderer.gameObject);
            else DestroyImmediate(grillRenderer.gameObject);
        }
        if (grillMaterial != null)
        {
            if (Application.isPlaying) Destroy(grillMaterial);
            else DestroyImmediate(grillMaterial);
        }
    }

    #region helper methods
    private void InitializeGauge()
    {
        maxHP = GetMaxHP();
        curHP = maxHP;
    }

    private int GetMaxHP()
    {
        if (catchable.Data is Ingredient ingredient)
        {
            IngredientStat ingredientStat = DataManager.Instance.GetIngredientStat().GetData(ingredient.statID);

            return ingredientStat.hp;
        }

        return 0;
    }

    private void PlayCutParticle()
    {
        Vector3 position = targetRenderer != null ? targetRenderer.bounds.center : transform.position;
        IngredientCutVfx.PlayAt(position, catchable != null ? catchable.Data as Ingredient : null);
    }

    /// <summary>Applies world-space bobbing and local rotation to the shared visual proxy only.</summary>
    public void SetConveyorVisualMotion(Vector3 worldOffset, Quaternion rotation)
    {
        if (!EnsureVisualProxy()) return;
        conveyorVisualActive = true;
        conveyorWorldOffset = worldOffset;
        conveyorVisualRotation = rotation;
        SyncVisualProxy();
    }

    public void ClearConveyorVisualMotion()
    {
        conveyorVisualActive = false;
        conveyorWorldOffset = Vector3.zero;
        conveyorVisualRotation = Quaternion.identity;
        if (motionPivot != null)
        {
            motionPivot.localRotation = Quaternion.identity;
            if (meshFilter != null && meshFilter.sharedMesh != null)
                motionPivot.localPosition = meshFilter.sharedMesh.bounds.center;
        }
        UpdateVisualProxyVisibility();
    }

    private bool EnsureVisualProxy()
    {
        CacheOriginalState();
        if (meshFilter == null || targetRenderer is not MeshRenderer sourceRenderer
            || meshFilter.transform != sourceRenderer.transform || meshFilter.sharedMesh == null)
            return false;
        if (motionPivot == null)
        {
            motionPivot = new GameObject("IngredientVisualMotion").transform;
            motionPivot.SetParent(meshFilter.transform, false);
            wobblePivot = new GameObject("CutHitWobblePivot").transform;
            wobblePivot.SetParent(motionPivot, false);
            var visual = new GameObject("IngredientMotionVisual");
            visual.transform.SetParent(wobblePivot, false);
            visual.layer = sourceRenderer.gameObject.layer;
            wobbleMeshFilter = visual.AddComponent<MeshFilter>();
            wobbleMeshRenderer = visual.AddComponent<MeshRenderer>();
            wobbleMeshRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            wobbleMeshRenderer.receiveShadows = sourceRenderer.receiveShadows;
            wobbleMeshRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            wobbleMeshRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            wobbleMeshRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            wobbleMeshRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            wobbleMeshRenderer.sortingOrder = sourceRenderer.sortingOrder;
            wobblePropertyBlock = new MaterialPropertyBlock();
            motionPivot.gameObject.SetActive(false);
        }
        return true;
    }

    private void SyncVisualProxy()
    {
        if (motionPivot == null || meshFilter == null || meshFilter.sharedMesh == null) return;
        bool wasVisible = motionPivot.gameObject.activeSelf;
        if (!wasVisible) presentationSourceWasEnabled = targetRenderer.enabled;
        wobbleMeshFilter.sharedMesh = meshFilter.sharedMesh;
        targetRenderer.GetSharedMaterials(presentationMaterials);
        wobbleMeshRenderer.GetSharedMaterials(proxyMaterials);
        bool changed = presentationMaterials.Count != proxyMaterials.Count;
        for (int i = 0; !changed && i < presentationMaterials.Count; i++)
            changed = presentationMaterials[i] != proxyMaterials[i];
        if (changed) wobbleMeshRenderer.SetSharedMaterials(presentationMaterials);
        targetRenderer.GetPropertyBlock(wobblePropertyBlock);
        wobbleMeshRenderer.SetPropertyBlock(wobblePropertyBlock);
        for (int i = 0; i < presentationMaterials.Count; i++)
        {
            targetRenderer.GetPropertyBlock(wobblePropertyBlock, i);
            wobbleMeshRenderer.SetPropertyBlock(wobblePropertyBlock, i);
        }
        Bounds bounds = meshFilter.sharedMesh.bounds;
        Vector3 bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        motionPivot.localPosition = bounds.center + meshFilter.transform.InverseTransformVector(conveyorWorldOffset);
        motionPivot.localRotation = conveyorVisualRotation;
        wobblePivot.localPosition = bottom - bounds.center;
        wobblePivot.GetChild(0).localPosition = -bottom;
        wobbleMeshRenderer.enabled = presentationSourceWasEnabled;
        UpdateVisualProxyVisibility();
    }

    private void UpdateVisualProxyVisibility()
    {
        if (motionPivot == null) return;
        bool visible = conveyorVisualActive || hitWobbleActive;
        if (visible)
        {
            motionPivot.gameObject.SetActive(true);
            targetRenderer.enabled = false;
        }
        else if (motionPivot.gameObject.activeSelf)
        {
            motionPivot.gameObject.SetActive(false);
            if (targetRenderer != null) targetRenderer.enabled = presentationSourceWasEnabled;
        }
    }

    private void PlayHitWobble()
    {
        if (!isActiveAndEnabled || wobbleAngle <= 0f || !EnsureVisualProxy()) return;
        if (wobbleRoutine != null) StopCoroutine(wobbleRoutine);
        hitWobbleActive = true;
        SyncVisualProxy();
        wobbleDirection = -wobbleDirection;
        wobbleRoutine = StartCoroutine(HitWobbleRoutine());
    }

    private IEnumerator HitWobbleRoutine()
    {
        float elapsed = 0f;
        while (elapsed < wobbleDuration)
        {
            float progress = Mathf.Clamp01(elapsed / wobbleDuration);
            float angle = Mathf.Sin(progress * Mathf.PI * 3f) * (1f - progress) * wobbleAngle * wobbleDirection;
            wobblePivot.localRotation = Quaternion.Euler(angle * 0.25f, 0f, angle);
            elapsed += Time.deltaTime;
            yield return null;
        }
        wobbleRoutine = null;
        StopHitWobble();
    }

    private void StopHitWobble()
    {
        if (wobbleRoutine != null) StopCoroutine(wobbleRoutine);
        wobbleRoutine = null;
        hitWobbleActive = false;
        if (wobblePivot != null) wobblePivot.localRotation = Quaternion.identity;
        UpdateVisualProxyVisibility();
    }
    #endregion

    #region Editor
#if UNITY_EDITOR
    public bool GrillPreview => grillPreview;

    public void SetGrillPreview(bool preview)
    {
        grillPreview = preview;
        if (preview) UpdateGrillOverlay();
        else ReleaseGrillOverlay();
    }
#endif
    #endregion
}
