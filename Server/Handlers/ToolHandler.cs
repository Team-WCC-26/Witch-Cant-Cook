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
        tool.Position = packet.Position;
        tool.Rotation = packet.Quaternion;

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
        tool.Position = packet.Position;
        tool.Rotation = packet.Quaternion;

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
            if (!room.Entities.TryGetValue(packet.EntityId, out var entity)) return;
            if (entity is not Dish dish) return;

            var DB = ServerContext.Instance.DataBase;

            packet.Success = DB.Dishes.TryGetValue(dish.IngredientId, out var dishId);

            // 추후 요리 내놓는 순서에 관한 기획나오면 id로 체크 => 지금은 레시피 존재만 하면 성공하는거로

            room.BroadCast(PacketSerializer.Serialize(packet, true));
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
