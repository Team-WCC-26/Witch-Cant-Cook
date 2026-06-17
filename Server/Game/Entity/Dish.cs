using Protocol;

namespace Server;

public class Dish : Entity, ICombinable
{
    public Food Food { get; private set; } = new();

    public IngredientStateData[] GetIngredients()
    {
        return Food.GetIngredients();
    }

    public bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        combinable = this;

        return Food.TryCombine(other, out _);
    }
}
