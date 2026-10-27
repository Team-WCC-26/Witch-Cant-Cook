using Protocol;

namespace Server;

public abstract class Tool() : Entity, IInteractable
{
    public int ToolId { get; private set; }
    public int Damage { get; private set; }

    public void InitToolId(int id)
    {
        ToolId = id;

        if (ServerContext.Instance.DataBase.Tools.TryGetValue(id, out var stat))
        {
            Damage = stat.Damage;
        }
    }

    public override void Destroy()
    {
    }

    public abstract bool Interact(Player player);

    // 로비 복귀 등 도구를 초기 상태로 되돌린다 (필요한 도구가 오버라이드한다)
    public virtual void Reset()
    {
    }

    private void Respawn()
    {
        Room.GenerateTool(ToolId, out long entityId);

        ToolSpawnPacket packet = new()
        {
            EntityId = entityId,
            ToolId = ToolId,
        };

        Room.BroadCast(PacketSerializer.Serialize(packet, true));
    }
}
