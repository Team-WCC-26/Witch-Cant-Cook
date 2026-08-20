namespace Server;

public abstract class ContainerTool(IContainerStorage storage) : Tool
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

        if (!_storage.TryInsert(entity)) return false;

        entity.Parent = this;

        return true;
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