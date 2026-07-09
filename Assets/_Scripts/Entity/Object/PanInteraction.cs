using System.Collections;
using UnityEngine;

public class PanInteraction : MonoBehaviour, IServePlate, IHeldPrimaryAction, IHeldObjectReceiver
{
    [SerializeField] private CatchableObj catchable;
    [SerializeField] private Transform ingredientSlot;
    [SerializeField] private float tossForce = 3f;
    [SerializeField] private float tossUpForce = 2f;

    private IngredientReaction currentIngredient;
    private Coroutine grillCoroutine;
    private StoveInteraction currentStove;

    public CatchableObj Catchable => catchable;
    public bool HasIngredient => currentIngredient != null;

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();
    }

    private void OnDisable()
    {
        StopGrill();
        currentStove?.Release(this);
        currentStove = null;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (currentIngredient != null) return;
        if (!TryGetIngredient(other, out IngredientReaction ingredient, out CatchableObj ingredientCatchable)) return;
        if (ingredientCatchable.IsHold) return;

        AttachIngredient(ingredient, ingredientCatchable);
    }

    public bool TryUsePrimary(PlayerInteract interact)
    {
        if (interact == null) return false;

        // Serve food first when the player is aiming at a plate.
        PlateInteraction plate = interact.FindInteractTarget<PlateInteraction>();
        if (plate != null && TryServePlate(plate))
            return true;

        // Place the pan on a stove when the player is aiming at one.
        StoveInteraction stove = interact.FindInteractTarget<StoveInteraction>();
        if (stove != null && stove.TryPlacePan(this, interact))
            return true;

        TossIngredient();
        return true;
    }

    public bool TryReceiveHeldObject(CatchableObj heldObj, PlayerInteract interact)
    {
        if (heldObj == null) return false;
        if (interact == null) return false;
        if (currentIngredient != null) return false;
        if (!heldObj.TryGetComponent(out IngredientReaction ingredient)) return false;
        if (!interact.TryReleaseHeld(heldObj)) return false;

        AttachIngredient(ingredient, heldObj);
        return true;
    }

    public bool TryServePlate(PlateInteraction plate)
    {
        if (plate == null) return false;
        if (currentIngredient == null) return false;

        plate.ShowTempFood();
        ConsumeIngredient();
        return true;
    }

    public void PlaceOnStove(StoveInteraction stove, Transform slot)
    {
        currentStove?.Release(this);
        currentStove = stove;

        AttachPan(slot);
        currentStove?.BeginCook(this);
    }

    public void ReleaseFromStove(StoveInteraction stove)
    {
        if (currentStove != stove) return;

        StopGrill();
        currentStove = null;
    }

    public void StartGrill(float duration)
    {
        if (currentIngredient == null) return;
        if (grillCoroutine != null) return;

        // Gauge is visual only; the coroutine owns completion.
        currentIngredient.GaugeUI?.StartFill(duration);
        grillCoroutine = StartCoroutine(GrillRoutine(duration));
    }

    public void StopGrill()
    {
        if (grillCoroutine != null)
        {
            StopCoroutine(grillCoroutine);
            grillCoroutine = null;
        }

        currentIngredient?.GaugeUI?.Hide();
    }

    private IEnumerator GrillRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        CompleteGrill();
    }

    private void CompleteGrill()
    {
        if (currentIngredient == null) return;

        currentIngredient.Interact(IngredientAction.Grill, int.MaxValue);
        grillCoroutine = null;
    }

    private void AttachPan(Transform slot)
    {
        if (catchable == null) return;
        if (slot == null) return;

        transform.position = slot.position;
        transform.rotation = slot.rotation;
        catchable.OnPlacedOnPrep(_ => currentStove?.Release(this));
    }

    private void AttachIngredient(IngredientReaction ingredient, CatchableObj ingredientCatchable)
    {
        currentIngredient = ingredient;

        // Keep the ingredient fixed inside the pan while it is stored.
        Transform slot = ingredientSlot != null ? ingredientSlot : transform;
        ingredient.transform.SetParent(slot, false);
        ingredient.transform.localPosition = Vector3.zero;
        ingredient.transform.localRotation = Quaternion.identity;

        ingredientCatchable.ChangePickState(false);
        ingredientCatchable.SetPhysicsState(false);

        currentStove?.BeginCook(this);
    }

    private void TossIngredient()
    {
        if (currentIngredient == null) return;

        // Restore physics and throw the ingredient out of the pan.
        IngredientReaction ingredient = currentIngredient;
        CatchableObj ingredientCatchable = ingredient.Catchable;
        StopGrill();
        currentIngredient = null;

        ingredient.transform.SetParent(null, true);
        ingredientCatchable.ChangePickState(true);
        ingredientCatchable.OnThrow();

        if (ingredientCatchable.Rb == null) return;

        Vector2 random = Random.insideUnitCircle.normalized;
        Vector3 direction = new Vector3(random.x, 0f, random.y);
        ingredientCatchable.Rb.linearVelocity = direction * tossForce + Vector3.up * tossUpForce;
        ingredientCatchable.Rb.angularVelocity = Vector3.zero;
    }

    private void ConsumeIngredient()
    {
        IngredientReaction ingredient = currentIngredient;
        StopGrill();
        currentIngredient = null;

        CatchableObj ingredientCatchable = ingredient.Catchable;
        ingredientCatchable.ChangePickState(false);
        ingredientCatchable.SetPhysicsState(false);

        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.Push(ingredientCatchable.gameObject);
            return;
        }

        ingredientCatchable.gameObject.SetActive(false);
    }

    private bool TryGetIngredient(Collider other, out IngredientReaction ingredient, out CatchableObj ingredientCatchable)
    {
        ingredient = other.GetComponentInParent<IngredientReaction>();
        ingredientCatchable = null;

        if (ingredient == null) return false;

        ingredientCatchable = ingredient.Catchable;
        if (ingredientCatchable == null) return false;
        if (ingredientCatchable.ObjType != CatchableObjType.Ingredient) return false;

        return true;
    }
}
