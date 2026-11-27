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

                // 재접속 유예 창 재생 목록 기록 (같은 샤드 작업 안에서 관측만 수행)
                room.RecordReplayEvent(new ReplayEventSnapshot()
                {
                    Kind = ReplayEventKind.PanEject,
                    EntityId = ejectPacket.PanEntityId,
                    RelatedEntityId = ejectPacket.IngredientEntityId,
                    Position = ejectPacket.Position,
                    Seed = ejectPacket.EjectSeed
                });
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

    [PacketHandler(PacketId.C_CostumeChange)]
    public static void ChangeCostume(Session session, PacketPackageInfo package)
    {
        if (session?.Player?.Room == null) return;

        var packet = DeSerialize<CostumeChangePacket>(package.Body);

        // 클라가 보낸 PlayerId는 신뢰하지 않고 세션의 플레이어로 덮어쓴다
        packet.PlayerId = session.Player.PlayerId;

        session.Player.PushGameplayJob(session, room =>
        {
            // 후발 참가자 동기화를 위해 서버에도 저장한다 (DB 연동 전까지 세션 수명 유지)
            session.Player.Costume = packet.Costume;

            room.BroadCast(PacketSerializer.Serialize(packet, true));
        });
    }
}
