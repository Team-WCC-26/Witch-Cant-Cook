namespace Protocol;

[AttributeUsage(AttributeTargets.Method)]
public class PacketHandlerAttribute : Attribute
{
    public PacketId PacketId { get; }

    public PacketHandlerAttribute(PacketId id)
    {
        PacketId = id;
    }
}
