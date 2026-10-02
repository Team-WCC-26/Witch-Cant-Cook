using Protocol;

namespace Server;

public abstract class Tool() : Entity, IInteractable
{
    public int ToolId { get; private set; }
    public int Damage { get; private set; }

    public void InitToolId(int id)
    {
        ToolId = id;

        ServerContext.Instance.DataBase.Tools.TryGetValue(id, out var stat);
        Damage = stat.Damage;
    }

    public override void Destroy()
    {
        base.Destroy();

        Room.TimerManager.Schedule(3000, this, static t => t.Respawn());
    }

    public abstract bool Interact(Player player);

    private void Respawn()
    {
        Room.GenerateTool(ToolId, out long entityId);

        ToolSpawnPacket packet = new()
        {
            EntityId = entityId,
            ToolId = ToolId,
        };

        Room.BroadCast(PacketSerializer.Serialize(packet));
    }
}
