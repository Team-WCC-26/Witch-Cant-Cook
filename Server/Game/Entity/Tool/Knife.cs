namespace Server;

public class Knife(int toolId) : Tool(toolId)
{
    public override bool Interact(Player player)
    {
        if (player.HoldingEntity != null) return false;

        player.HoldingEntity = this;
        Parent = player;

        return true;
    }
}
