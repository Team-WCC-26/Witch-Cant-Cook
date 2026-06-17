using Protocol;

namespace Server;

public class Ingredient(int ingredientId) : Entity, ICombinable, ICookable
{
    public int IngredientId => _ingredientId;
    private readonly int _ingredientId = ingredientId;
    public IngredientState ProcessState { get; set; } = 0;

    public bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        combinable = null;

        switch (other)
        {
            case Ingredient ingredient:
                var DB = ServerContext.Instance.DataBase;

                if (!DB.RecipeDict.TryGetValue(new(this, ingredient), out var resId)) return false;

                combinable = new Ingredient(resId);
                return true;

            case Dish d:
                d.TryCombine(this, out combinable);
                return true;
        }

        return false;
    }

    public bool TryCook(IngredientState state, out Ingredient ingredient)
    {
        ingredient = this;

        if ((ProcessState & state) != 0) return false;

        var DB = ServerContext.Instance.DataBase;

        if ((DB.Ingredients[IngredientId].InvalidProcessFlag & state) != 0) return false;

        ProcessState |= state;

        return true;
    }
}
