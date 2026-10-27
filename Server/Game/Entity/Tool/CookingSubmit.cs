using Protocol;

namespace Server;

public class CookingSubmit() : ContainerTool(new MultiSlotStorage())
{
    private const long _ejectDelayMs = 1000;

    public override bool Interact(Player player)
    {
        if (_storage.Count == 0) return false;

        foreach (var entity in _storage)
        {
            if (entity is not Dish dish) continue;
            if (dish.Ingredient is not Ingredient ingredient) continue;

            Room.SubmitDish(ingredient.IngredientId);
            ingredient.Destroy();   // 접시의 SingleSlotStorage에서 제거되어 빈 접시가 된다
        }

        List<Entity> ejectEntities = new(_storage);
        Clear();

        BroadCastEject(ejectEntities);

        return true;
    }

    private void BroadCastEject(List<Entity> entities)
    {
        CookingSubmitEjectPacket packet = new()
        {
            CookingSubmitEntityId = EntityId,
            EjectSeed = (int)TimeUtil.NowMs(),
            EjectDelayMs = _ejectDelayMs
        };

        foreach (var entity in entities)
        {
            packet.EntityIds.Add(entity.EntityId);
        }

        Room.BroadCast(PacketSerializer.Serialize(packet, true));
    }
}
