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
        var ingredient = room.GenerateIngredient(packet.IngredientID);

        packet.EntityId = ingredient.EntityId;
        ingredient.Position = packet.Position;
        ingredient.Rotation = packet.Quaternion;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_IngredientCut)]
    public static void CutIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<CutIngredientPacket>(package.Body);
        var room = session.Player.Room;

        if (room.Entityes[packet.EntityID] is not Ingredient ingredient) return;
        if ((DB.Ingredients[ingredient.IngredientId].ConditionFlag & IngredientState.Cut) != 0) return;

        ingredient.ProcessState |= IngredientState.Cut;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_IngredientGrill)]
    public static void GrillIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<GrillIngredientPacket>(package.Body);
        var room = session.Player.Room;

        if (room.Entityes[packet.EntityID] is not Ingredient ingredient) return;
        if ((DB.Ingredients[ingredient.IngredientId].ConditionFlag & IngredientState.Grilled) != 0) return;

        ingredient.ProcessState |= IngredientState.Grilled;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_IngredientCancelGrill)]
    public static void CancelGrillIngredient(Session session, PacketPackageInfo package)
    {

    }

    [PacketHandler(PacketId.C_IngredientBoil)]
    public static void BoilIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<BoilIngredientPacket>(package.Body);
        var room = session.Player.Room;

        if (room.Entityes[packet.EntityID] is not Ingredient ingredient) return;
        if ((DB.Ingredients[ingredient.IngredientId].ConditionFlag & IngredientState.Boiled) != 0) return;

        ingredient.ProcessState |= IngredientState.Boiled;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_IngredientRoast)]
    public static void RoastIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<RoastIngredientPacket>(package.Body);
        var room = session.Player.Room;

        if (room.Entityes[packet.EntityID] is not Ingredient ingredient) return;
        if ((DB.Ingredients[ingredient.IngredientId].ConditionFlag & IngredientState.Roasted) != 0) return;

        ingredient.ProcessState |= IngredientState.Roasted;

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }

    [PacketHandler(PacketId.C_IngredientCombine)]
    public static void CombineIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<IngredientCombinePacket>(package.Body);
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
