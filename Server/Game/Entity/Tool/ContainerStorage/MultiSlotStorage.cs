
using System.Collections;

namespace Server;

public class MultiSlotStorage : IContainerStorage
{
    private readonly List<Entity> _items = new();

    public int Count => _items.Count;

    public Entity? First => _items.Count > 0 ? _items[0] : null;

    public bool TryInsert(Entity entity)
    {
        if (entity == null) return false;
        if (_items.Contains(entity)) return false;

        _items.Add(entity);

        return true;
    }

    public bool TryRemove(Entity entity)
    {
        return _items.Remove(entity);
    }

    public void Clear()
    {
        _items.Clear();
    }

    public bool Contains(Entity entity)
    {
        return _items.Contains(entity);
    }

    public IEnumerator<Entity> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
