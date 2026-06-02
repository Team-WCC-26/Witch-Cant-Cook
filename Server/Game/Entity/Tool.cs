namespace Server;

public class Tool : Entity
{
    public Tool(long id, int toolId) : base(id)
    {
        ToolId = toolId;
    }

    public readonly int ToolId;
}
