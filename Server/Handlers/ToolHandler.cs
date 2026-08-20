using Protocol;

namespace Server;

public class ToolHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_ToolSpawn)]
    public static void SpawnTool(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<ToolSpawnPacket>(package.Body);
        var room = session.Player.Room;
        var tool = room.GenerateTool(packet.ToolId, out var entityId);

        packet.EntityId = entityId;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_ToolRegister)]
    public static void RegisterTool(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<ToolRegisterPacket>(package.Body);
        var room = session.Player.Room;
        var tool = room.GenerateTool(packet.ToolId, out var entityId);

        packet.EntityId = entityId;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_ServeDish)]
    public static void ServeDish(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<ServeDishPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            room.ServeDish(packet.EntityId);
        });
    }

    [PacketHandler(PacketId.C_ClearDish)]
    public static void ClearDish(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<ServeDishPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            if (!room.Entities.TryGetValue(packet.EntityId, out var entity)) return;
            if (entity is not Dish dish) return;

            dish.Clear();

            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }
}
