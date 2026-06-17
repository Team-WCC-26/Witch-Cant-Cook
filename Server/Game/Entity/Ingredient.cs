using Protocol;

namespace Server;

public class Ingredient(int ingredientId) : Entity, ICombinable, ICookable
{
    public readonly int IngredientId = ingredientId;
    public IngredientState ProcessState { get; set; } = 0;

    public bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        switch (other)
        {
            case Ingredient ingredient:
                combinable = Food.Create(this, ingredient);
                break;

            case Food f:
                combinable = f;
                f.Ingredients.Add(new(IngredientId, ProcessState));
                break;

            case Dish d:
                d.TryCombine(this, out combinable);
                break;

            default:
                combinable = null;
                return false;
        }

        return true;
    }

    public IngredientStateData[] GetIngredients()
    {
        return [ new IngredientStateData
        {
            Id = IngredientId,
            StateFlag = ProcessState
        }];
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
