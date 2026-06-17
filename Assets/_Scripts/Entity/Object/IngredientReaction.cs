using System;
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

public class IngredientReaction : MonoBehaviour
{
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
    public float CurGuage => 1 - (float)curHP / maxHP;
    public InteractionGaugeUI GaugeUI => gaugeUI;

    [Header("Plate Offset")]
    [SerializeField] private Vector3 plateOffsetPos = Vector3.zero;
    [SerializeField] private Vector3 plateOffsetEuler = Vector3.zero;
    public Vector3 PlateOffsetPos => plateOffsetPos;
    public Vector3 PlateOffsetEuler => plateOffsetEuler;

    [Header("Action Visuals")]
    [SerializeField] private IngredientActionVisual[] actionVisuals;

    private void Awake()
    {
        InitializeGauge();

        // TODO: Load mesh and shader data from ResourceManager.
    }

    private void OnEnable()
    {
        InitializeGauge();
        lastAction = IngredientAction.None;
        completedActions = IngredientAction.None;

        ApplyVisual(IngredientAction.None);
    }

    public bool Interact(IngredientAction action, int dmg = 0)
    {
        if (IsActionBlocked(action))
        {
            Debug.Log("Action is blocked for this ingredient.");
            return false;
        }

        if (lastAction != action)
        {
            lastAction = action;
            curHP = maxHP; // Reset HP when action changes
        }
        else if (curHP <= 0)
        {
            Debug.Log("Action already completed.");
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
                    Debug.Log("Cutting Completed.");
                    break;
                case IngredientAction.Grill:
                    Debug.Log("Grilling Completed.");
                    break;
                case IngredientAction.Boil:
                    Debug.Log("Boiling Completed.");
                    break;
                case IngredientAction.Cook:
                    Debug.Log("Cooking Completed.");
                    break;
                default:
                    Debug.Log("Unknown action.");
                    break;
            }

            completedActions |= action;
            ApplyVisual(completedActions);
            return true;
        }

        return false;
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
    #endregion
}
