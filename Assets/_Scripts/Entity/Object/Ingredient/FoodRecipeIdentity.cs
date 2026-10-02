using UnityEngine;

// Runtime identity comes from the spawn packet's Recipe row, never from the rendered prefab.
[DisallowMultipleComponent]
public sealed class FoodRecipeIdentity : MonoBehaviour, IPoolable
{
    [SerializeField] private int recipeId;
    public int RecipeId => recipeId;

    public static void Initialize(CatchableObj food, Ingredient ingredient, Recipe recipe)
    {
        if (food == null) return;
        FoodRecipeIdentity identity = food.GetComponent<FoodRecipeIdentity>();
        if (recipe != null)
        {
            if (identity == null) identity = food.gameObject.AddComponent<FoodRecipeIdentity>();
            identity.recipeId = recipe.id;
            food.Data = new CatchableData { id = recipe.id, name = recipe.name, prefabName = recipe.prefabName };
        }
        else
        {
            if (identity != null) identity.ResetForPool();
            food.Data = ingredient;
        }
    }

    public void ResetForPool() => recipeId = 0;
}
