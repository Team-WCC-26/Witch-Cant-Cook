using Protocol;

namespace Server;

public class TrashCan() : ContainerTool(new MultiSlotStorage())
{
    private const long _ejectDelayMs = 1000;

    public override bool Interact(Player player)
    {
        if (_storage.Count == 0 && InsidePlayers.Count == 0) return false;

        List<Entity> targets = new(_storage);

        foreach (var entity in targets)
        {
            entity.Destroy();
        }

        List<Entity> ejectEntities = new(_storage);
        Clear();

        List<Player> ejectPlayers = new(InsidePlayers);
        ClearInsidePlayers();

        if (ejectEntities.Count > 0 || ejectPlayers.Count > 0)
        {
            BroadCastEject(ejectEntities, ejectPlayers);
        }

        return true;
    }

    private void BroadCastEject(List<Entity> entities, List<Player> players)
    {
        TrashCanEjectPacket packet = new()
        {
            TrashCanEntityId = EntityId,
            EjectSeed = (int)TimeUtil.NowMs(),
            EjectDelayMs = _ejectDelayMs
        };

        foreach (var player in players)
        {
            packet.PlayerIds.Add(player.PlayerId);
        }

        foreach (var entity in entities)
        {
            packet.EntityIds.Add(entity.EntityId);
        }

        Room.BroadCast(PacketSerializer.Serialize(packet, true));
    }
}
