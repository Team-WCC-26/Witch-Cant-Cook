namespace Server;

public abstract class Tool(int toolId) : Entity, IInteractable
{
    public readonly int ToolId = toolId;

    public abstract bool Interact(Player player);
}
