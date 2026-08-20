namespace Server;

public interface IContainerStorage : IEnumerable<Entity>
{
    int Count { get; }
    Entity? First { get; }
    bool TryInsert(Entity entity);
    bool TryRemove(Entity entity);
    void Clear();
    bool Contains(Entity entity);
}
