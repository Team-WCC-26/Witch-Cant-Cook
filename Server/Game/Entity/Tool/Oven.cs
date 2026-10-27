using Protocol;

namespace Server;

public class Oven() : CookingTool(new MultiSlotStorage()), IFixedTool
{
    protected override IngredientState _cookState => IngredientState.Roasted;

    public override bool Interact(Player player) // 원래 인터페이스를 분리시키는게 맞으나 귀찮으니 클라에서 오븐시작 버튼누른것만 반응하게 해야함
    {
        if (StartCook())
        {
            StartPlayerDamage(); // 조리가 시작된 순간부터 초당 데미지
        }

        return true;
    }

    public override bool Insert(Entity entity)
    {
        if (entity is IFixedTool) return false;
        if (!_storage.TryInsert(entity)) return false;

        entity.Parent = this;

        return true;
    }

    protected override void Cook()
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

        // 조리 완료 시 오븐 안의 플레이어는 사망 처리한다 (부활로 멤버십이 변하므로 복사본을 순회한다)
        List<Player> insidePlayers = new(InsidePlayers);

        foreach (var player in insidePlayers)
        {
            player.Kill();
        }

        StopPlayerDamage(); // 조리가 끝나면 데미지 중단
    }
}
