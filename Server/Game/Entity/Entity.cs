using System.Numerics;

namespace Server;

public class Entity
{
    public Entity(long id)
    {
        EntityId = id;
    }

    public readonly long EntityId;
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
}
