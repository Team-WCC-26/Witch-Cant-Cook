using System.Collections;

namespace Server;

public class SingleSlotStorage : IContainerStorage
{
    private Entity? _item;

    public int Count => _item == null ? 0 : 1;

    public Entity? First => _item;

    public bool TryInsert(Entity entity)
    {
        if (entity == null) return false;
        if (_item != null) return false;

        _item = entity;

        return true;
    }

    public bool TryRemove(Entity entity)
    {
        if (entity == null) return false;
        if (_item != entity) return false;

        _item = null;

        return true;
    }

    public void Clear()
    {
        _item = null;
    }

    public bool Contains(Entity entity)
    {
        return _item == entity;
    }

    public IEnumerator<Entity> GetEnumerator()
    {
        if (_item != null)
            yield return _item;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
