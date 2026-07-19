using Protocol;

namespace Server;

public class Ingredient(int ingredientId) : Entity, ICombinable, ICookable, IInteractable
{
    public int IngredientId => _ingredientId;
    private readonly int _ingredientId = ingredientId;
    public IngredientState ProcessState { get; set; } = 0;

    private int _hp = -1;
    public int Hp
    {
        get
        {
            if (_hp < 0)
            {
                if (ServerContext.Instance.DataBase.TryGetIngredientStatById(IngredientId, out var stat))
                {
                    _hp = stat.Hp;
                }
                else
                {
                    _hp = 10;
                }
            }

            return _hp;
        }
    }

    public bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        combinable = null;

        switch (other)
        {
            case Ingredient ingredient:
                var DB = ServerContext.Instance.DataBase;

                if (!DB.IngredientCombinations.TryGetValue(new(this, ingredient), out var resId)) return false;

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

    public bool Interact(Player player)
    {
        if (player.HoldingEntity is not ICombinable combinable) return false;

        TryCombine(combinable, out var res);

        return true;
    }
}
