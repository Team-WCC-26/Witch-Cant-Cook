using Protocol;

namespace Server;

public class Ingredient(int ingredientId) : Entity, ICombinable, ICookable
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
