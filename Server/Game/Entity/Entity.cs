using Protocol;

namespace Server;

public abstract class Entity
{
    public long EntityId { get; private set; }
    public Room Room { get; private set; }
    public Entity? Parent
    {
        get => _parent;
        set
        {
            if (_parent == value) return;

            if (_parent is ContainerTool ct)
            {
                ct.Remvoe(this);
            }
            else if (_parent is Player player)
            {
                player.HoldingEntity = null;
            }

            _parent = value;

            MakeDirty(DirtyMask.Parent);
        }
    }

    public bool IsDestroyed => _dirtyMask.HasFlag(DirtyMask.Destroy);

    private Entity? _parent;
    private DirtyMask _dirtyMask = DirtyMask.None;

    internal void InitEntityId(long id)
    {
        EntityId = id;
        _dirtyMask = DirtyMask.None;
    }

    internal void AttachRoom(Room room)
    {
        Room = room;
    }

    public void Destroy()
    {
        Parent = null;
        MakeDirty(DirtyMask.Destroy);
    }

    public DirtyMask ConsumeDirtyMask()
    {
        var mask = _dirtyMask;
        _dirtyMask = DirtyMask.None;

        return mask;
    }

    public virtual void WriteSnapShot(WorldStatePacket packet, DirtyMask mask)
    {
        if (mask.HasFlag(DirtyMask.Destroy))
        {
            Room.UnregisterEntity(EntityId);

            packet.DestroyedEntities.Add(new()
            {
                EntityId = EntityId,
            });
        }

        if (mask.HasFlag(DirtyMask.Parent))
        {
            if (Parent == null) return;

            if (Parent is Player player)
            {
                packet.PickupEntities.Add(new()
                {
                    EntityId = EntityId,
                    PlayerID = player.PlayerId
                });
            }
            else
            {
                packet.ParentChangedEntities.Add(new()
                {
                    EntityId = EntityId,
                    ParentEntityId = Parent.EntityId
                });
            }
        }
    }

    protected void MakeDirty(DirtyMask mask)
    {
        if ((_dirtyMask & mask) == mask) return;

        _dirtyMask |= mask;

        Room?.MakeDirty(this);
    }
}

[Flags]
public enum DirtyMask
{
    None = 0,

    // Entity Lifecycle
    Destroy = 1 << 0,

    // Hierachy
    Parent = 1 << 1,

    // Transform
    Transform = 1 << 2,

    // GamePlay State
    Ping = 1 << 3,
    State = 1 << 4,
    Process = 1 << 5,
}
