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

    [PacketHandler(PacketId.C_IngredientCut)]
    public static void CutIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<CutIngredientPacket>(package.Body);
        var room = session.Player.Room;

        if (room.Entities[packet.EntityID] is not Ingredient ingredient) return;
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

        if (room.Entities[packet.EntityID] is not Ingredient ingredient) return;
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

        if (room.Entities[packet.EntityID] is not Ingredient ingredient) return;
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

        if (room.Entities[packet.EntityID] is not Ingredient ingredient) return;
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
        var room = session.Player.Room;
        var entities = room.Entities;

        if (packet.SubjectEntityId == packet.TargetEntityId) return;

        if (!entities.TryGetValue(packet.SubjectEntityId, out var subject)) return;
        if (subject is not ICombinable sc) return;

        if (!entities.TryGetValue(packet.TargetEntityId, out var target)) return;
        if (target is not ICombinable tc) return;

        if (!tc.TryCombine(sc, out var resultFood)) return;

        room.CombineIngredient(packet.TargetEntityId, packet.SubjectEntityId, resultFood);

        IngredientCombineResultPacket combineResultPacket = new()
        {
            FoodEntityId = packet.TargetEntityId,
            RemovedEntityId = packet.SubjectEntityId,
            Ingredients = resultFood.Ingredients.Select(x => new IngredientStateData
            {
                Id = x.IngredientId,
                StateFlag = x.ProcessState
            }).ToArray()
        };

        room.PushJob(() =>
        {
            room.BroadCast(PacketSerializer.Serialize(combineResultPacket, true));
        });
    }

    [PacketHandler(PacketId.C_IngredientPut)]
    public static void PutIngredient(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<IngredientPutPacket>(package.Body);
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
