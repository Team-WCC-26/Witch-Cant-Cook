namespace Server;

public class Dish() : ContainerTool(new SingleSlotStorage())
{
    public int IngredientId => (Ingredient != null) ? Ingredient.IngredientId : -1;
    public Ingredient? Ingredient => First as Ingredient;

    public override bool Interact(Player player)
    {
        if (player.HoldingEntity != null) return Insert(player.HoldingEntity);

        player.HoldingEntity = this;
        Parent = player;

        return true;
    }

    public override bool Insert(Entity entity)
    {
        if (Parent is Pot pot && pot.IsCooking) return false; // 조리 중인 Dish에는 삽입 불가
        if (entity is not Ingredient ingredient) return false;
        if (First != null) return false;

        if (!_storage.TryInsert(ingredient)) return false;

        ingredient.Parent = this;

        return true;
    }
}
