using Protocol;

namespace Server;

public class Oven(int toolId) : CookingTool(toolId, new MultiSlotStorage()), IFixedTool
{
    protected override IngredientState _cookState => IngredientState.Roasted;

    public override bool Interact(Player player)
    {
        StartCook();

        return true;
    }

    public override bool Insert(Entity entity)
    {
        if (entity is IFixedTool) return false;

        return _storage.TryInsert(entity);
    }

    protected override void Cook(CookingTool tool)
    {
        foreach (var item in _storage)
        {
            Ingredient ingredient;

            if (item is Dish dish)
            {
                ingredient = dish.Ingredient;
            }
            else
            {
                ingredient = item as Ingredient;
            }

            if (ingredient == null) continue;

            ingredient.TryCook(_cookState);
        }
    }
}
