using UnityEngine;

public class PlateInteraction : MonoBehaviour, IEntityParentReceiver
{
    private CatchableObj currentFood;

    public bool IsEmpty => currentFood == null;

    private void OnEnable()
    {
        currentFood = null;
    }

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

    public void HandleEntityRemoved(CatchableObj entity)
    {
        if (entity == null || entity != currentFood) return;

        entity.transform.SetParent(null, true);
        currentFood = null;
    }
}
