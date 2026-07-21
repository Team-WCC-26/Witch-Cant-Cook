using System.Numerics;

namespace Server;

public abstract class Entity
{
    public Room Room { get; private set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }

    private DirtyMask _dirtyMask = DirtyMask.None;

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
        if (mask.HasFlag(DirtyMask.Position))
        {
            
        }

        if (mask.HasFlag(DirtyMask.Rotation))
        {

        }
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
