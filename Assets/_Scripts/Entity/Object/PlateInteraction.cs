using UnityEngine;

public class PlateInteraction : MonoBehaviour, IHeldPrimaryAction
{
    [SerializeField] private GameObject tempFoodVisual;

    private void OnEnable()
    {
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
