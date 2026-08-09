namespace Server;

public class CounterTop(int toolId) : ContainerTool(toolId, new SingleSlotStorage()), IFixedTool
{
    //public override bool TryCombine(ICombinable other, out ICombinable combinable)
    //{
    //    combinable = other;

    //    if (other == null) return false;

    //    if (Entity == null)
    //    {
    //        Entity = other as Entity;

    //        return true;
    //    }

    //    if (other is Dish)
    //    {
    //        if (!other.TryCombine(Entity as ICombinable, out combinable)) return false;

    //        Clear();
    //    }
    //    else
    //    {
    //        if (!TryCombine(other, out combinable)) return false;

    //        Entity = combinable as Entity;
    //    }

    //    return true;
    //}
    public override bool Interact(Player player)
    {
        if (player.HoldingEntity == null) return false;

        return _storage.TryInsert(player.HoldingEntity);
    }
}
