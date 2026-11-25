public interface IHeldObjectReceiver
{
    bool TryReceiveHeldObject(CatchableObj heldObj, PlayerInteract interact);
}

// Ingredient receivers share the same read-only eligibility query with the HUD.
public interface IHeldIngredientReceiver : IHeldObjectReceiver
{
    bool CanReceiveHeldIngredient(CatchableObj heldObj);
}
