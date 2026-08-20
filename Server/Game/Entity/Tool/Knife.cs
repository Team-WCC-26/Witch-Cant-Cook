namespace Server;

public class Knife : Tool
{
    public override bool Interact(Player player)
    {
        if (player.HoldingEntity != null) return false;

        player.HoldingEntity = this;
        Parent = player;

        return true;
    }
}
