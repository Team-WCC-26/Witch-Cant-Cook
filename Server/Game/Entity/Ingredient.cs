using Protocol;

namespace Server;

public class Ingredient : Entity
{
    public Ingredient(long entityId, int ingredientId) : base(entityId)
    {
        IngredientId = ingredientId;
    }

    public readonly int IngredientId;
    public IngredientState ProcessState { get; set; } = 0;
}
