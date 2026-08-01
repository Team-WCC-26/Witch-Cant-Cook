namespace Server;

public class Dish(int toolId) : ContainerTool(toolId, new SingleSlotStorage())
{
    public int IngredientId => (Ingredient != null) ? Ingredient.IngredientId : -1;
    public Ingredient? Ingredient => _storage.First as Ingredient;

    public override bool Interact(Player player)
    {
        if (player.HoldingEntity == null)
        {
            player.HoldingEntity = this;
            return true;
        }

        if (player.HoldingEntity is Dish dish)
        {
            if (dish.Ingredient == null)
            {
                if (dish.Insert(Ingredient))
                {
                    _storage.Clear();
                }
            }
            else
            {
                var ingredient = dish.Ingredient;
                dish.Clear();

                if (!_storage.TryInsert(ingredient))
                {
                    Ingredient.TryCombine(ingredient, out var res); // 합치기 실패인 경우 쓰레기가 나올지 아님 합쳐지지 않도록 할지 구분 필요
                    dish.Insert(res);
                }
            }
        }
        else if (player.HoldingEntity is Ingredient ingredient)
        {
            if (!_storage.TryInsert(ingredient))
            {
                Ingredient.TryCombine(ingredient, out var res);
                _storage.Clear();
                _storage.TryInsert(res);
            }
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
