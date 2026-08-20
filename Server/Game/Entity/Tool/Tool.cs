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

    public abstract bool Interact(Player player);
}
