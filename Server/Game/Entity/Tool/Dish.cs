namespace Server;

public class Dish(int toolId) : ContainerTool(toolId, new SingleSlotStorage())
{
    public int IngredientId => (Ingredient != null) ? Ingredient.IngredientId : -1;
    public Ingredient? Ingredient => First as Ingredient;

    public override bool Interact(Player player)
    {
        if (player.HoldingEntity == null)
        {
            player.HoldingEntity = this;
            Parent = player;
        }
        else if (player.HoldingEntity is Dish dish)
        {
            if (TryCombine(dish.Ingredient))
            {
                dish.Clear();
            }
            else if(dish.TryCombine(Ingredient))
            {
                _storage.Clear();
            }

            return false;
        }
        else if (player.HoldingEntity is Pan pan)
        {
            if (!TryCombine(pan.Ingredient)) return false;
        }
        else if (player.HoldingEntity is Ingredient ingredient)
        {
            TryCombine(ingredient);
        }
        else
        {
            return false;
        }

        return true;
    }

    public override bool Insert(Entity entity)
    {
        return false; // insert 없음
    }

    public bool TryCombine(Ingredient ingredient)
    {
        if (ingredient == null) return false;

        if (!_storage.TryInsert(ingredient))
        {
            Ingredient.TryCombine(ingredient, out var result);
            _storage.Clear();
            _storage.TryInsert(result);
        }

        return true;
    }

    //public override bool TryCombine(ICombinable other, out ICombinable combinable)
    //{
    //    combinable = this;

    //    if (other is Dish) return false;

    //    if (Ingredient != null && !Ingredient.TryCombine(other, out other)) return false;

    //    Entity = other as Entity;

    //    return true;
    //}
}
