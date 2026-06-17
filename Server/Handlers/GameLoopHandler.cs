using Protocol;

namespace Server;

public class GameLoopHandler : PacketHandlerBase
{
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
}
