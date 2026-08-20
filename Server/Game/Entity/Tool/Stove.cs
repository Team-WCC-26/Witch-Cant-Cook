namespace Server;

public class Stove() : ContainerTool(new SingleSlotStorage()), IFixedTool
{
    //public override bool TryCombine(ICombinable other, out ICombinable combinable)
    //{
    //    combinable = other;

    //    if (Entity != null) return false;
    //    if (other is not Pan pan) return false;

    //    Entity = pan;

    //    pan.SetCookEnable(true);
    //    pan.StartCook();

    //    return true;
    //}

    public override bool Interact(Player player)
    {
        return Insert(player.HoldingEntity);
    }

    public override bool Insert(Entity entity)
    {
        if (entity is Pan pan && _storage.TryInsert(pan))
        {
            pan.Parent = this;
            pan.SetCookEnable(true);
            pan.StartCook();

            return true;
        }

        return false;
    }
}
