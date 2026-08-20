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

            SetCookEnable(false);
            
            if (_timerManager.Pause(_cookTimer))
            {
                CookPausePacket packet = new()
                {
                    ToolEntityId = EntityId,
                };

                Room.BroadCast(PacketSerializer.Serialize(packet, true));
            }

            return true;
        }

        if (player.HoldingEntity is Dish dish && _timerManager.RemainingTime(_cookTimer) <= 0)
        {
            return dish.TryCombine(Ingredient);
        }

        return Insert(player.HoldingEntity);
    }

    public override bool Insert(Entity entity)
    {
        if (!base.Insert(entity)) return false;

        StartCook();

        return true;
    }
}