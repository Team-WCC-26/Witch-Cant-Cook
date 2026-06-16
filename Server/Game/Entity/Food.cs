using Protocol;

namespace Server;

public class Food : Entity, ICombinable, ICookable
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

    public bool TryCook(IngredientState state, out Ingredient ingredient)
    {
        ingredient = null;
        var DB = ServerContext.Instance.DataBase;

        if (!DB.RecipeDict.TryGetValue(new(Ingredients), out var ingredientId)) return false;
        if ((DB.Ingredients[ingredientId].InvalidProcessFlag & state) != 0) return false;

        ingredient = new(ingredientId);
        ingredient.ProcessState |= state;

        return true;
    }
}
