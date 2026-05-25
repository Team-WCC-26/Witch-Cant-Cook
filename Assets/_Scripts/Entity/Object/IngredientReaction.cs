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

    [Header("임시 메시")]
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private Mesh defaultMesh;
    [SerializeField] private Mesh cutMesh;

    public void Interact(IngredientAction action)
    {
        if (IsActionBlocked(action))
        {
            Debug.Log("Action is blocked for this ingredient.");
            return;
        }

        switch (action)
        {
            case IngredientAction.Cut:
                Debug.Log("Cutting the ingredient.");
                ApplyMesh(cutMesh);
                break;
            case IngredientAction.Grill:
                Debug.Log("Grilling the ingredient.");
                break;
            case IngredientAction.Boil:
                Debug.Log("Boiling the ingredient.");
                break;
            case IngredientAction.Cook:
                Debug.Log("Cooking the ingredient.");
                break;
            default:
                Debug.Log("Unknown action.");
                break;
        }
    }

    private bool IsActionBlocked(IngredientAction action)
    {
        if (catchable.Data is not Ingredient ingredient)
            return false;

        return ((IngredientAction)ingredient.conditionFlag & action) != 0;
    }

    private void ApplyMesh(Mesh mesh = null)
    {
        if (mesh == null)
        {
            meshFilter.mesh = defaultMesh;
            return;
        }

        meshFilter.mesh = mesh;
    }
}