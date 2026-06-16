namespace Server;

public class Food : Entity, ICombinable
{
    public readonly HashSet<IngredientStatePair> Ingredients = new();

    public bool TryCombine(ICombinable other, out Food food)
    {
        food = this;

        switch (other)
        {
            case Ingredient i:
                Ingredients.Add(new(i.IngredientId, i.ProcessState));
                break;

            case Food f:
                Ingredients.UnionWith(f.Ingredients);
                break;

            default:
                return false;
        }

        return true;
    }

    public static Food Create(Ingredient a, Ingredient b)
    {
        Food food = new();

        food.Ingredients.Add(new(a.IngredientId, a.ProcessState));

        food.Ingredients.Add(new(b.IngredientId, b.ProcessState));

        return food;
    }
}
