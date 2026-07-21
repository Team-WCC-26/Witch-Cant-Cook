namespace Server;

public class Pan(int toolId) : CookingTool(toolId)
{
    public override bool Interact(Player player)
    {
        throw new NotImplementedException();
    }

    protected override void Cook(CookingTool tool)
    {
        throw new NotImplementedException();
    }
}