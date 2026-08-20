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

    [PacketHandler(PacketId.C_EntityInteract)]
    public static void InteractEntity(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<EntityInteractPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            room.InteractEntity(packet.TargetEntityId, session.Player);
        });
    }

    [PacketHandler(PacketId.C_EntityInsert)]
    public static void InsertEntity(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<EntityInsertPacket>(package.Body);
        var room = session.Player.Room;

        room.PushJob(() =>
        {
            room.InsertEntity(packet.TargetEntityId, packet.SubjectEntityId);
        });
    }

    //[PacketHandler(PacketId.C_EntityPickup)]
    //public static void PickupEntity(Session session, PacketPackageInfo package)
    //{
    //    var packet = DeSerialize<EntityPickupPacket>(package.Body);
    //    var room = session.Player.Room;

    //    room.PushJob(() =>
    //    {
    //        room.BroadCast(PacketSerializer.Serialize(packet, true));
    //    });
    //}

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

    //[PacketHandler(PacketId.C_EntityCombine)]
    //public static void CombineEntity(Session session, PacketPackageInfo package)
    //{
    //    var packet = DeSerialize<EntityCombinePacket>(package.Body);
    //    var room = session.Player.Room;
    //    var entities = room.Entities;

    //    room.PushJob(() =>
    //    {
    //        if (packet.SubjectEntityId == packet.TargetEntityId) return;

    //        if (!entities.TryGetValue(packet.SubjectEntityId, out var subject)) return;
    //        if (subject is not ICombinable sc) return;

    //        if (!entities.TryGetValue(packet.TargetEntityId, out var target)) return;
    //        if (target is not ICombinable tc) return;

    //        EntityCombineResultPacket combineResultPacket = new()
    //        {
    //            TargetEntityId = packet.TargetEntityId,
    //            SubjectEntityId = packet.SubjectEntityId
    //        };

    //        if (combineResultPacket.Success = tc.TryCombine(sc, out var combinable))
    //        {
    //            long remainId, removedId;

    //            if (subject is Dish)
    //            {
    //                remainId = packet.SubjectEntityId;
    //                removedId = packet.TargetEntityId;
    //            }
    //            else
    //            {
    //                remainId = packet.TargetEntityId;
    //                removedId = packet.SubjectEntityId;
    //            }

    //            room.CombineEntity(remainId, removedId, combinable as Entity);

    //            combineResultPacket.RemainingEntityId = remainId;
    //            combineResultPacket.RemovedEntityId = removedId;
    //            combineResultPacket.ResultIngredientId = combinable.IngredientId;
    //        }

    //        room.BroadCast(PacketSerializer.Serialize(combineResultPacket, true));
    //    });
    //}

    //[PacketHandler(PacketId.C_EntityPut)]
    //public static void PutEntity(Session session, PacketPackageInfo package)
    //{
    //    var packet = DeSerialize<EntityPutPacket>(package.Body);
    //    var room = session.Player.Room;

    //    room.PushJob(() =>
    //    {
    //        room.BroadCast(PacketSerializer.Serialize(packet, true));
    //    });
    //}
}
