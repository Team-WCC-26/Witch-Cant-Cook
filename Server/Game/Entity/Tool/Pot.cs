namespace Server;

public class Pot(int toolId) : CookingTool(toolId)
{
    public override bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        throw new NotImplementedException();
    }
}
