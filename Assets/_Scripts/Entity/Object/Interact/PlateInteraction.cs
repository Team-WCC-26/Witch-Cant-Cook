using UnityEngine;

public class PlateInteraction : MonoBehaviour, IEntityParentReceiver, IPoolable, IPanPrimaryReceiver
{
    private CatchableObj currentFood;

    public bool IsEmpty => currentFood == null;

    // 팬에 든 음식을 빈 그릇으로 전달받는다.
    public bool TryReceivePanPrimary(PanInteraction pan, PlayerInteract player)
    {
        if (pan == null) return false;
        if (player == null) return false;

        return pan.TryMoveToPlate(this);
    }

    // Pool 반환 시 음식 정리
    public void ResetForPool()
    {
        if (currentFood == null) return;

        CatchableObj food = currentFood;
        currentFood = null;
        food.transform.SetParent(null, true);

        if (ObjectPoolManager.Instance != null)
            ObjectPoolManager.Instance.Push(food.gameObject);
    }

    // 완성 음식 그릇 귀속
    public void HandleEntityAdded(CatchableObj entity)
    {
        // Parent result
        if (entity == null || !entity.TryGetComponent(out IngredientReaction reaction)) return;

        currentFood = entity;
        entity.ChangePickState(false);
        entity.SetPhysicsState(false);
        entity.transform.SetParent(transform, false);
        entity.transform.localPosition = reaction.PlateOffsetPos;
        entity.transform.localRotation = Quaternion.Euler(reaction.PlateOffsetEuler);
    }

    // 귀속 음식 분리
    public void HandleEntityRemoved(CatchableObj entity)
    {
        if (entity == null || entity != currentFood) return;

        entity.transform.SetParent(null, true);
        currentFood = null;
    }
}
