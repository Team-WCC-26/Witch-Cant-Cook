using System;
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

    [Header("Interact Gauge")]
    [SerializeField] private InteractionGaugeUI gaugeUI;
    private IngredientAction lastAction = IngredientAction.None;
    private IngredientAction completedActions = IngredientAction.None;
    private int curHP;
    private int maxHP;
    private float lastCutProcess;
    private Coroutine wobbleRoutine;
    private Transform wobblePivot;
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

    [Header("Action Visuals")]
    [SerializeField] private IngredientActionVisual[] actionVisuals;

    #region Unity Callbacks

    private void Awake()
    {
        CacheOriginalScale();
        InitializeGauge();
    }

    private void OnDisable()
    {
        StopHitWobble();
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
    }

    public void RestoreOriginalScale()
    {
        CacheOriginalScale();
        transform.localScale = originalScale;
    }

    public void ResetForPool()
    {
        RestoreOriginalScale();
        StopHitWobble();
        InitializeGauge();
        lastAction = IngredientAction.None;
        completedActions = IngredientAction.None;
        lastCutProcess = 0f;
        wobbleDirection = 1f;

        gaugeUI?.Hide();
        ApplyVisual(IngredientAction.None);
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
            StopHitWobble();
            gaugeUI?.Hide();
            PlayCutParticle();
        }
        ApplyVisual(completedActions);
        if (newlyCompleted != IngredientAction.None)
            OnActionCompleted?.Invoke(newlyCompleted);
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
    }

    private void ApplyMaterials(Material[] materials)
    {
        if (materials == null) return;
        if (materials.Length == 0) return;
        if (targetRenderer == null) return;

        targetRenderer.sharedMaterials = materials;
    }
    #endregion

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

    private void PlayHitWobble()
    {
        if (!isActiveAndEnabled || wobbleAngle <= 0f || meshFilter == null ||
            targetRenderer is not MeshRenderer sourceRenderer ||
            meshFilter.transform != sourceRenderer.transform ||
            meshFilter.sharedMesh == null)
            return;

        if (wobblePivot == null)
        {
            GameObject pivotObject = new GameObject("CutHitWobblePivot");
            wobblePivot = pivotObject.transform;
            wobblePivot.SetParent(meshFilter.transform, false);

            GameObject visualObject = new GameObject("CutHitWobbleVisual");
            visualObject.transform.SetParent(wobblePivot, false);
            visualObject.layer = sourceRenderer.gameObject.layer;
            wobbleMeshFilter = visualObject.AddComponent<MeshFilter>();
            wobbleMeshRenderer = visualObject.AddComponent<MeshRenderer>();
            wobbleMeshRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            wobbleMeshRenderer.receiveShadows = sourceRenderer.receiveShadows;
            wobbleMeshRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            wobbleMeshRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            wobbleMeshRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            wobbleMeshRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            wobbleMeshRenderer.sortingOrder = sourceRenderer.sortingOrder;
            wobblePropertyBlock = new MaterialPropertyBlock();
        }

        if (wobbleRoutine != null)
            StopCoroutine(wobbleRoutine);

        wobbleMeshFilter.sharedMesh = meshFilter.sharedMesh;
        wobbleMeshRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        for (int i = 0; i < wobbleMeshRenderer.sharedMaterials.Length; i++)
        {
            sourceRenderer.GetPropertyBlock(wobblePropertyBlock, i);
            wobbleMeshRenderer.SetPropertyBlock(wobblePropertyBlock, i);
        }

        Bounds bounds = meshFilter.sharedMesh.bounds;
        Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        wobblePivot.localPosition = pivot;
        wobblePivot.GetChild(0).localPosition = -pivot;
        wobblePivot.localRotation = Quaternion.identity;
        wobblePivot.gameObject.SetActive(true);
        sourceRenderer.enabled = false;

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
        if (wobbleRoutine != null)
        {
            StopCoroutine(wobbleRoutine);
            wobbleRoutine = null;
        }

        if (wobblePivot == null || !wobblePivot.gameObject.activeSelf) return;
        wobblePivot.localRotation = Quaternion.identity;
        wobblePivot.gameObject.SetActive(false);
        if (targetRenderer != null)
            targetRenderer.enabled = true;
    }
    #endregion
}
