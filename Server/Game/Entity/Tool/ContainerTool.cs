namespace Server;

public abstract class ContainerTool(int toolId) : Tool(toolId), ICombinable
{
    public Entity? Entity { get; protected set; }

    public abstract bool TryCombine(ICombinable other, out ICombinable combinable);

    public override bool Interact(Player player)
    {
        if (player.HoldingEntity == null || player.HoldingEntity is not ICombinable combinable) return false;

        return TryCombine(combinable, out _);
    }

    public bool Insert(Entity entity)
    {
        if (entity is IFixedTool) return false;

        return true;
    }

    public void Clear()
    {
        Entity = null;
    }
}