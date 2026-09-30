using System.Collections.Generic;

// A combination ID identifies a row; a recipe ID identifies a group of rows.
// Preserve row order and repeated ingredients (which may have different cooking flags).
public sealed class RecipeIngredientIndex
{
    private readonly Dictionary<int, List<IngredientCombination>> rowsByRecipe = new();
    public RecipeIngredientIndex(IEnumerable<IngredientCombination> rows)
    {
        foreach (IngredientCombination row in rows)
        {
            if (row == null) continue;
            if (!rowsByRecipe.TryGetValue(row.recipeID, out var group))
                rowsByRecipe.Add(row.recipeID, group = new List<IngredientCombination>());
            group.Add(row);
        }
    }
    public IReadOnlyList<IngredientCombination> GetRows(int recipeId) =>
        rowsByRecipe.TryGetValue(recipeId, out var rows) ? rows : System.Array.Empty<IngredientCombination>();
}
