namespace Server;

public abstract class Entity
{
    public long EntityId { get; private set; }
    public Room Room { get; private set; }
    public Entity? Parent
    {
        get; 
        set
        {
            if (Parent is ContainerTool ct)
            {
                ct.Remvoe(this);
            }

            Parent = value;
        }
    }

    private DirtyMask _dirtyMask = DirtyMask.None;

    internal void InitEntityId(long id)
    {
        EntityId = id;
    }

    internal void AttachRoom(Room room)
    {
        Room = room;
    }

    public DirtyMask ConsumeDirtyMask()
    {
        var mask = _dirtyMask;
        _dirtyMask = DirtyMask.None;

        return mask;
    }

    public virtual void WriteSnapShot(PacketBatch batch, DirtyMask mask)
    {
    }

    protected void MakeDirty(DirtyMask mask)
    {
        if ((_dirtyMask & mask) == mask) return;

        _dirtyMask |= mask;

        Room.MakeDirty(this);
    }
}

[Flags]
public enum DirtyMask
{
    None = 0,
    Position = 1 << 0,
    Rotation = 1 << 1,
    State = 1 << 2,
    Container = 1 << 3,
}
