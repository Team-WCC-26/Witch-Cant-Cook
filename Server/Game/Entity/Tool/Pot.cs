namespace Server;

public class Pot(int toolId) : CookingTool(toolId)
{
    public override bool Interact(Player player)
    {
        throw new NotImplementedException();
    }

    public override bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        throw new NotImplementedException();
    }

    protected override void Cook(CookingTool tool)
    {
        throw new NotImplementedException();
    }
}
