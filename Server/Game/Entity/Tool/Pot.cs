using Protocol;

namespace Server;

public class Pot(int toolId) : CookingTool(toolId, new SingleSlotStorage()), IFixedTool
{
    protected override IngredientState _cookState => IngredientState.Boiled;

    public override bool Interact(Player player)
    {
        if (player.HoldingEntity is not Dish dish) return false;
        if (!dish.Ingredient.TryCombine(Ingredient, out var res)) return false;

        dish.Clear();
        return dish.Insert(res as Entity);
    }

    public override bool Insert(Entity entity)
    {
        if (entity is not Ingredient subject) return false;

        if (!base.Insert(entity))
        {
            if (!Ingredient.TryCombine(subject, out var result)) return false; // false가 아닌 res를 쓰레기로 db연결시켜야할듯

            _storage.Clear();
            _storage.TryInsert(result);
        }

        StartCook();

        return true;
    }

    protected override void Cook(CookingTool tool)
    {
        if (!Ingredient.TryCook(IngredientState.Boiled))
        {
            // Ingredient를 쓰레기로 바꾸는 로직 넣거나 trycook 내부에서 불가능한 조리법일시 쓰레기로 바꾸도록 해야할듯
        }
    }
}
