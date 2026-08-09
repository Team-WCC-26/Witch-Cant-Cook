using Protocol;

namespace Server;

public class Ingredient(int ingredientId) : Entity, ICookable, IInteractable
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

    public bool TryCombine(Ingredient other, out Ingredient result)
    {
        result = null;

        var DB = ServerContext.Instance.DataBase;

        if (!DB.IngredientCombinations.TryGetValue(new(this, other), out var resId)) return false;

        result = Room.GenerateIngredient(resId, out _);

        other.Room.UnregisterEntity(other.EntityId);
        Room.UnregisterEntity(EntityId);

        return true;
    }

    public bool TryCook(IngredientState state)
    {
        if ((ProcessState & state) != 0) return false;

        var DB = ServerContext.Instance.DataBase;

        if ((DB.Ingredients[IngredientId].InvalidProcessFlag & state) != 0) return false;

        ProcessState |= state;

        return true;
    }

    public bool Interact(Player player)
    {
        if (player.HoldingEntity == null)
        {
            player.HoldingEntity = this;
            Parent = player;

            return true;
        }

        if (player.HoldingEntity is Knife) return TryCook(IngredientState.Cut);
        if (player.HoldingEntity is Dish dish)
        {
            return dish.TryCombine(this);
        }

        //if (player.HoldingEntity is not ICombinable combinable) return false;

        //TryCombine(combinable, out var res);

        return false;
    }
}
