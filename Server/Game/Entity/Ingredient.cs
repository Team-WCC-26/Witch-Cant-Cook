using Protocol;

namespace Server;

public class Ingredient(int ingredientId) : Entity, ICombinable
{
    public readonly int IngredientId = ingredientId;
    public IngredientState ProcessState { get; set; } = 0;

    public bool TryCombine(ICombinable other, out Food food)
    {
        switch (other)
        {
            case Ingredient ingredient:
                food = Food.Create(this, ingredient);
                break;

            case Food f:
                food = f;
                food.Ingredients.Add(new(IngredientId, ProcessState));
                break;

            default:
                food = null;
                return false;
        }

        return true;
    }
}
