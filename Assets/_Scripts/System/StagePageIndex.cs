using System;
using System.Collections.Generic;

// Pages and protocol requests use Stage.stageNum, never Stage.id or list positions.
public sealed class StagePageIndex
{
    private readonly List<int> stageIds = new();
    private readonly Dictionary<int, Stage> stagesByNumber = new();
    private readonly Dictionary<int, List<int>> ingredientsByStage = new();
    public IReadOnlyList<int> StageIds => stageIds;

    public StagePageIndex(IEnumerable<Stage> stages, IEnumerable<IngredientGroup> ingredientRows)
    {
        if (stages != null)
        {
            foreach (Stage stage in stages)
            {
                if (stage == null || stage.stageNum <= 0 || stagesByNumber.ContainsKey(stage.stageNum)) continue;
                stagesByNumber.Add(stage.stageNum, stage);
                stageIds.Add(stage.stageNum);
            }
        }
        stageIds.Sort();
        if (ingredientRows == null) return;
        foreach (IngredientGroup row in ingredientRows)
        {
            if (row == null || row.ingredient1 <= 0 || !stagesByNumber.ContainsKey(row.difficulty)) continue;
            if (!ingredientsByStage.TryGetValue(row.difficulty, out var ingredients))
                ingredientsByStage.Add(row.difficulty, ingredients = new List<int>());
            if (!ingredients.Contains(row.ingredient1)) ingredients.Add(row.ingredient1);
        }
    }

    public bool TryGetStage(int stageNum, out Stage stage) => stagesByNumber.TryGetValue(stageNum, out stage);

    public int IndexOf(int stageId) => stageIds.IndexOf(stageId);

    public bool TryGetAdjacentStage(int stageId, int direction, out int adjacentStageId)
    {
        adjacentStageId = stageId;
        int index = IndexOf(stageId);
        int adjacentIndex = index + Math.Sign(direction);
        if (direction == 0 || index < 0 || adjacentIndex < 0 || adjacentIndex >= stageIds.Count) return false;
        adjacentStageId = stageIds[adjacentIndex];
        return true;
    }

    public IReadOnlyList<int> GetIngredientIds(int stageId) =>
        ingredientsByStage.TryGetValue(stageId, out var ingredients) ? ingredients : Array.Empty<int>();
}
