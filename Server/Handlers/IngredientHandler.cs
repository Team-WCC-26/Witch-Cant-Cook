using Protocol;

namespace Server;

public class IngredientHandler : PacketHandlerBase
{
    public static DataBase DB => ServerContext.Instance.DataBase;

    [PacketHandler(PacketId.C_IngredientSpawn)]
    public static void SpawnIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<IngredientSpawnPacket>(package.Body);
        var room = session.Player.Room;
        var ingredient = room.GenerateIngredient(packet.IngredientID, out var entityId);

        packet.EntityId = entityId;
        ingredient.Position = packet.Position;
        ingredient.Rotation = packet.Quaternion;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    //[PacketHandler(PacketId.C_CookStart)]
    //public static void StartCook(Session session, PacketPackageInfo package)
    //{
    //    var packet = DeSerialize<CookStartPacket>(package.Body);
    //    var room = session.Player.Room;

    //    room.PushJob(() =>
    //    {
    //        if (room.Entities[packet.EntityId] is not ICookable cookable) return;
    //        if (!cookable.TryCook(packet.CookType, out var ingredient)) return;

    //        room.UpdateEntity(packet.EntityId, ingredient);

    //        CookCompletePacket completePacket = new() // 임시로 바로 완료 패킷 보냄 => 추후에 타입별로 다르게 처리해야함
    //        {
    //            EntityId = packet.EntityId,
    //            IngredientId = ingredient.IngredientId,
    //            CookType = packet.CookType
    //        };

    //        room.BroadCast(PacketSerializer.Serialize(completePacket, true));
    //    });
    //}

    //[PacketHandler(PacketId.C_CookCancel)]
    //public static void CancelCook(Session session, PacketPackageInfo package)
    //{
    //    var packet = DeSerialize<CookCancelPacket>(package.Body);
    //    var room = session.Player.Room;

    //    room.PushJob(() =>
    //    {
    //        room.BroadCast(PacketSerializer.Serialize(packet, true));
    //    });
    //}

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
