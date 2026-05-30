using System;
using UnityEngine;

[Flags]
public enum IngredientAction : byte
{
    None = 0,

    Cut = 1,        // 자르기 
    Grill = 2,      // 굽기 
    Boil = 4,       // 삶기 
    Cook = 8        // 익히기
}

public class IngredientReaction : MonoBehaviour
{
    [SerializeField] private CatchableObj catchable;
    public CatchableObj Catchable => catchable;

    [Header("Components")]
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private Renderer targetRenderer;

    [Header("Interact Gauge")]
    private IngredientAction lastAction = IngredientAction.None;
    private int curHP;
    private int maxHP;
    public float CurGuage => 1 - (float)curHP / maxHP;

    #region 재료 변화 캐싱
    [SerializeField] private Mesh defaultMesh;
    [SerializeField] private Mesh cutMesh;
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Material cookedMaterial;
    #endregion

    private void Awake()
    {
        maxHP = GetMaxHP();
        curHP = maxHP;

        //TODO : ResourceManager에서 Mesh, Shader 불러오기
        //defaultMesh = ResourceManager.Instance.GetAsset<Mesh>("IngredientMesh");
        //cutMesh = ResourceManager.Instance.GetAsset<Mesh>("CutIngredientMesh");
        //defaultShader = ResourceManager.Instance.GetAsset<Shader>("DefaultShader");
        //cookedShader = ResourceManager.Instance.GetAsset<Shader>("CookedShader");
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

        //TODO : HP 감소에 따른 Mesh, Shader 변화 구현하기
        //TODO : 완료된 플래그 표시해주기
        if (curHP <= 0)
        {
            switch (action)
            {
                case IngredientAction.Cut:
                    Debug.Log("Cutting Completed.");
                    ApplyMesh(cutMesh); //TODO : dmg 반영하기
                    break;
                case IngredientAction.Grill:
                    Debug.Log("Grilling Completed.");
                    break;
                case IngredientAction.Boil:
                    Debug.Log("Boiling Completed.");
                    break;
                case IngredientAction.Cook:
                    ApplyMaterial(cookedMaterial);
                    break;
                default:
                    Debug.Log("Unknown action.");
                    break;
            }

            return true; //Action 완료 시 Interact 호출 금지용
        }

        return false;
    }
    
    private bool IsActionBlocked(IngredientAction action)
    {
        if (catchable.Data is not Ingredient ingredient)
            return false;

        return ((IngredientAction)ingredient.conditionFlag & action) != 0;
    }

    #region 재료 변화 적용
    private void ApplyMesh(Mesh mesh = null)
    {
        if (mesh == null)
        {
            meshFilter.mesh = defaultMesh;
            return;
        }

        meshFilter.mesh = mesh;
    }

    private void ApplyMaterial(Material material = null)
    {
        //default shader
         if (material == null)
        {
            targetRenderer.material = defaultMaterial;
            return;
        }

        targetRenderer.material = material;
    }
    #endregion

    #region helper methods
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