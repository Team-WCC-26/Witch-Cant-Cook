using Protocol;

namespace Server;

public class Pan() : CookingTool(new SingleSlotStorage())
{
    protected override IngredientState _cookState => IngredientState.Grilled;

    public override bool Interact(Player player)
    {
        if (player.HoldingEntity == null)
        {
            player.HoldingEntity = this;
            Parent = player;

            PauseCook();

            return true;
        }

        if (player.HoldingEntity is Dish dish && _timerManager.RemainingTime(_cookTimer) <= 0)
        {
            return dish.Insert(Ingredient);
        }

        return Insert(player.HoldingEntity);
    }

    public override bool Insert(Entity entity)
    {
        if (!base.Insert(entity)) return false;

        StartCook();

        return true;
    }

    public bool LeaveStove()
    {
        if (Parent is not Stove) return false;

        Parent = null;

        PauseCook();

        return true;
    }

    private void PauseCook()
    {
        SetCookEnable(false);

        if (_timerManager.Pause(_cookTimer))
        {
            CookPausePacket pausePacket = new()
            {
                ToolEntityId = EntityId
            };

            Room.BroadCast(PacketSerializer.Serialize(pausePacket, true));
        }
    }
}