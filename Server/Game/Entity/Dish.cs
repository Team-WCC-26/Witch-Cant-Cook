using Protocol;

namespace Server;

public class Dish : Entity, ICombinable
{
    public int IngredientId => (Ingredient != null) ? Ingredient.IngredientId : -1;
    public Ingredient Ingredient { get; private set; }

    public bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        combinable = this;

        if (other is Dish) return false;

        if (Ingredient != null && !Ingredient.TryCombine(other, out other)) return false;

        Ingredient = other as Ingredient;

        return true;
    }

    public void Clear()
    {
        Ingredient = null;
    }
}
