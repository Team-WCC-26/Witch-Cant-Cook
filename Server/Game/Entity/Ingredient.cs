using Protocol;

namespace Server;

public class Ingredient : Entity
{
    public Ingredient(long entityId, int ingredientId) : base(entityId)
    {
        IngredientIds.Add(ingredientId);
    }

    public readonly HashSet<int> IngredientIds = new();
    public IngredientState ProcessState { get; set; } = 0;
}
