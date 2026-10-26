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

        if (ejectEntities.Count > 0)
        {
            BroadCastEject(ejectEntities);
        }

        List<Player> insidePlayers = new(InsidePlayers);

        foreach (var insidePlayer in insidePlayers)
        {
            insidePlayer.Respawn();
        }

        return true;
    }

    private void BroadCastEject(List<Entity> entities)
    {
        TrashCanEjectPacket packet = new()
        {
            TrashCanEntityId = EntityId,
            EjectSeed = (int)TimeUtil.NowMs(),
            EjectDelayMs = _ejectDelayMs
        };

        foreach (var entity in entities)
        {
            packet.EntityIds.Add(entity.EntityId);
        }

        Room.BroadCast(PacketSerializer.Serialize(packet, true));
    }
}
