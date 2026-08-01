namespace Server;

public class Stove(int toolId) : ContainerTool(toolId, new SingleSlotStorage()), IFixedTool
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
        if (Insert(player.HoldingEntity))
        {
            player.HoldingEntity = null;

            return true;
        }

        return false;
    }

    public override bool Insert(Entity entity)
    {
        if (entity is Pan pan && _storage.TryInsert(pan))
        {
            pan.SetCookEnable(true);

            return true;
        }

        return false;
    }
}
