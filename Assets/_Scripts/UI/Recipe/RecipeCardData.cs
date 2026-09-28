using System;
using System.Collections.Generic;
using Protocol;
using UnityEngine;

[Serializable]
public class RecipeIngredientDisplayData
{
    public string iconName;
    public string name;
    public Sprite sprite;
    public IngredientState conditionFlag;
    public int amount = 1;
}

// UI input contract populated from the Recipe sheet, with optional local artwork.
[Serializable]
public class RecipeCardData
{
    public string iconName;
    public int recipeId;
    public string name;
    public Sprite sprite;
    public IngredientState finalConditionFlag;
    [Min(0.01f)] public float timeLimit = 60f; // Seconds.
    public List<RecipeIngredientDisplayData> ingredients = new();
}


