using Protocol;

namespace Server;

public class Pan(int toolId) : CookingTool(toolId, new SingleSlotStorage())
{
    protected override IngredientState _cookState => IngredientState.Grilled;

    public override bool Interact(Player player)
    {
        if (player.HoldingEntity == null)
        {
            player.HoldingEntity = this;
            return true;
        }

        if (player.HoldingEntity is not Ingredient ingredient) return false;
        if (!_storage.TryInsert(ingredient)) return false;

        StartCook();

        return true;
    }
}