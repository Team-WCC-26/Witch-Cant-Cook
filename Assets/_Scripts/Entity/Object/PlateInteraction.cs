using UnityEngine;

public class PlateInteraction : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        IngredientReaction reaction = collision.gameObject.GetComponent<IngredientReaction>();

        if (reaction != null)
        {
            CatchableObj catchable = reaction.Catchable;
            catchable.Col.enabled = false;
            catchable.SetPhysicsState(false);
            catchable.ChangePickState(false);

            reaction.transform.SetParent(transform, true);
            reaction.transform.localPosition = reaction.PlateOffsetPos;
            reaction.transform.localRotation = Quaternion.Euler(reaction.PlateOffsetEuler);
        }
    }
}