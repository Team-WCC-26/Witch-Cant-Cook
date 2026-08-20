using Protocol;

namespace Server;

public class Pot() : CookingTool(new SingleSlotStorage()), IFixedTool
{
    protected override IngredientState _cookState => IngredientState.Boiled;

    public override bool Insert(Entity entity)
    {
        if (entity is not Ingredient subject) return false;

        if (!base.Insert(entity))
        {
            Ingredient.TryCombine(subject, out var result);
            _storage.Clear();
            _storage.TryInsert(result);
            result.Parent = this;
        }

        StartCook();

        return true;
    }
}
