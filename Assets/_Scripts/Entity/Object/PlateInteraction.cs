using UnityEngine;

public class PlateInteraction : MonoBehaviour, IHeldPrimaryAction, IEntityParentReceiver
{
    [SerializeField] private GameObject tempFoodVisual;

    private CatchableObj currentFood;

    private void OnEnable()
    {
        currentFood = null;
        if (tempFoodVisual == null) return;

        tempFoodVisual.SetActive(false);
        DisableTempFoodColliders();
    }

    public void ShowTempFood()
    {
        if (tempFoodVisual == null) return;

        tempFoodVisual.SetActive(true);
        DisableTempFoodColliders();
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

        if (tempFoodVisual != null)
            tempFoodVisual.SetActive(false);
    }

    public void HandleEntityRemoved(CatchableObj entity)
    {
        if (entity == null || entity != currentFood) return;

        entity.transform.SetParent(null, true);
        currentFood = null;
    }

    private void DisableTempFoodColliders()
    {
        foreach (Collider foodCollider in tempFoodVisual.GetComponentsInChildren<Collider>(true))
        {
            foodCollider.enabled = false;
        }
    }

    public bool TryUsePrimary(PlayerInteract interact)
    {
        if (interact == null) return false;

        return interact.TryServePlate(this);
    }

    private void OnCollisionEnter(Collision collision)
    {
        //IngredientReaction reaction = collision.gameObject.GetComponent<IngredientReaction>();

        //if (reaction != null)
        //{
        //    CatchableObj catchable = reaction.Catchable;
        //    catchable.Col.enabled = false;
        //    catchable.SetPhysicsState(false);
        //    catchable.ChangePickState(false);

        //    reaction.transform.SetParent(transform, true);
        //    reaction.transform.localPosition = reaction.PlateOffsetPos;
        //    reaction.transform.localRotation = Quaternion.Euler(reaction.PlateOffsetEuler);
        //}
    }
}
