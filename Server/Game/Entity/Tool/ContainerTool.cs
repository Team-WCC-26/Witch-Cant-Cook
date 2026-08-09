namespace Server;

public abstract class ContainerTool(int toolId, IContainerStorage storage) : Tool(toolId)
{
    public Entity? First => _storage.First;
    protected IContainerStorage _storage { get; init; } = storage;

    //public override bool Interact(Player player)
    //{
    //    if (player.HoldingEntity == null || player.HoldingEntity is not ICombinable combinable) return false;

    //    return TryCombine(combinable, out _);
    //}

    public virtual bool Insert(Entity entity)
    {
        if (entity is IFixedTool) return false;

        return _storage.TryInsert(entity);
    }

    public bool Remvoe(Entity entity)
    {
        return _storage.TryRemove(entity);
    }

    public void Clear()
    {
        _storage.Clear();
    }
}