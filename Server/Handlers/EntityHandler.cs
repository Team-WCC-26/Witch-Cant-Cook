using Protocol;

namespace Server;

public class EntityHandler : PacketHandlerBase
{

    [PacketHandler(PacketId.C_EntityDestroy)]
    public static void DestroyEntity(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<EntityDestroyPacket>(package.Body);
        var room = session.Player.Room;

        room.DestroyIngredient(packet.EntityId);

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }


    [PacketHandler(PacketId.C_EntityPickup)]
    public static void PickupEntity(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<EntityPickupPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_EntityThrow)]
    public static void ThrowEntity(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<EntityThrowPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }
}
