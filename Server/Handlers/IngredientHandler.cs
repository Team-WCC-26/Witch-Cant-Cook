using Protocol;

namespace Server;

public class IngredientHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_IngredientSpawn)]
    public static void SpawnIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<IngredientSpawnPacket>(package.Body);
        var room = session.Player.Room;

        packet.EntityId = room.GenerateEntityId();

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_IngredientDestroy)]
    public static void DestroyIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<IngredientDestroyPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }


    [PacketHandler(PacketId.C_IngredientPickup)]
    public static void PickupIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<IngredientPickupPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_IngredientThrow)]
    public static void ThrowIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<IngredientThrowPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    //[PacketHandler(PacketId.C_IngredientState)]
    public static void UpdateIngredientState(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<IngredientStatePacket>(package.Body);
        var room = session.Player.Room;

        // hp에 따라 state 변경 로직 추가

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }
}
