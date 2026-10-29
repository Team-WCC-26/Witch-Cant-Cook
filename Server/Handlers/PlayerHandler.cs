using Protocol;

namespace Server;

public class PlayerHandler : PacketHandlerBase
{
    [PacketHandler(PacketId.C_PlayerMove)]
    public static void UpdateMove(Session session, PacketPackageInfo package)
    {
        var packet = DeSerialize<PlayerMovementPacket>(package.Body);

        session.Player.PushGameplayJob(session, room =>
        {
            session.Player.Position = packet.Position;
            session.Player.Rotation = packet.Rotation;
            session.Player.State = packet.CombinedState;

            if (packet.CombinedState.Interaction == PlayerInteraction.Drop &&
                session.Player.HoldingEntity is Pan pan &&
                !pan.IsDestroyed &&
                pan.Ingredient is Ingredient ingredient &&
                !ingredient.IsDestroyed)
            {
                ingredient.Parent = null;

                pan.CancelCook(); // 재료가 배출되었으므로 진행 중인 조리를 중지한다

                PanEjectPacket ejectPacket = new()
                {
                    PanEntityId = pan.EntityId,
                    IngredientEntityId = ingredient.EntityId,
                    Position = session.Player.Position,
                    EjectSeed = (int)TimeUtil.NowMs()
                };

                room.BroadCast(PacketSerializer.Serialize(ejectPacket, true));
            }
        });
    }

    [PacketHandler(PacketId.C_PlayerDamage)]
    public static void DamagePlayer(Session session, PacketPackageInfo package) // TODO => 본인이 맞았다고 판정된것만 적용할건지 정해야함
    {
        var packet = DeSerialize<PlayerDamagePacket>(package.Body);

        if (packet.PlayerId != session.Player.PlayerId) return;

        session.Player.PushGameplayJob(session, room =>
        {
            session.Player.ApplyDamage(Math.Min(packet.Damage, Player.MaxHp));
        });
    }
}
