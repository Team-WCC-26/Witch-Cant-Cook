using Protocol;

namespace Server;

public class Dish : Entity, ICombinable
{
    public int IngredientId => Ingredient.IngredientId;
    public Ingredient Ingredient { get; private set; }

    public bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        combinable = this;

        if (other is Dish) return false;
        if (!Ingredient.TryCombine(other, out var res)) return false;

        Ingredient = res as Ingredient;

        return true;
    }
}
