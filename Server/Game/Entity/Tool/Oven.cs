using Protocol;

namespace Server;

public class Oven(int toolId) : CookingTool(toolId, new MultiSlotStorage()), IFixedTool
{
    protected override IngredientState _cookState => IngredientState.Roasted;

    public override bool Interact(Player player) // 원래 인터페이스를 분리시키는게 맞으나 귀찮으니 클라에서 오븐시작 버튼누른것만 반응하게 해야함
    {
        StartCook();

        return true;
    }

    public override bool Insert(Entity entity)
    {
        if (entity is IFixedTool) return false;

        return _storage.TryInsert(entity);
    }

    protected override void Cook(CookingTool tool)
    {
        foreach (var item in _storage)
        {
            Ingredient ingredient;

            if (item is ContainerTool ct) // 제대로 예외처리할려면 더 넣어줘야하지만 지금 구조상으론 이정도도 충분
            {
                ingredient = ct.First as Ingredient;
            }
            else
            {
                ingredient = item as Ingredient;
            }

            if (ingredient == null) continue;

            ingredient.TryCook(_cookState);
        }
    }
}
