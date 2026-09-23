using System;
using System.Collections.Generic;
using Protocol;
using UnityEngine;

[Serializable]
public class RecipeIngredientDisplayData
{
    public string name;
    public Sprite sprite;
    public IngredientState conditionFlag;
}

// UI input contract populated from the Recipe sheet, with optional local artwork.
[Serializable]
public class RecipeCardData
{
    public int recipeId;
    public string name;
    public Sprite sprite;
    public IngredientState finalConditionFlag;
    [Min(0.01f)] public float timeLimit = 60f; // Seconds.
    public List<RecipeIngredientDisplayData> ingredients = new();
}

[CreateAssetMenu(menuName = "Scriptable Objects/Cooking Visual/Recipe UI Catalog")]
public class RecipeVisualCatalog : ScriptableObject
{
    [Serializable]
    private class MethodIcon
    {
        public IngredientState method;
        public Sprite sprite;
    }

    // Retained only for previously assigned sprites/ingredient artwork. Never used for gameplay data.
    [HideInInspector]
    [SerializeField] private List<RecipeCardData> recipes = new();
    [Serializable]
    private class RecipeArtwork
    {
        public int recipeId;
        public Sprite sprite;
    }
    [SerializeField] private List<RecipeArtwork> recipeArtwork = new();
    [SerializeField] private List<MethodIcon> methodIcons = new();
    private readonly HashSet<int> warnedRecipes = new();
    private Dictionary<int, RecipeCardData> lookup;

    private void OnEnable() { lookup = null; warnedRecipes.Clear(); }
    private void OnValidate() { lookup = null; warnedRecipes.Clear(); }

    public Sprite GetRecipeSprite(int recipeId)
    {
        foreach (RecipeArtwork art in recipeArtwork)
            if (art != null && art.recipeId == recipeId) return art.sprite;
        if (lookup == null)
        {
            lookup = new Dictionary<int, RecipeCardData>();
            foreach (RecipeCardData recipe in recipes)
            {
                if (recipe == null) continue;
                if (lookup.ContainsKey(recipe.recipeId))
                    Debug.LogWarning($"Duplicate RecipeId {recipe.recipeId}; first entry wins.", this);
                else lookup.Add(recipe.recipeId, recipe);
            }
        }
        if (lookup.TryGetValue(recipeId, out RecipeCardData data)) return data.sprite;
        return null;
    }

    public Sprite GetMethodIcon(IngredientState method)
    {
        foreach (MethodIcon icon in methodIcons)
            if (icon != null && icon.method == method) return icon.sprite;
        return null;
    }
}
