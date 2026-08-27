using System.Collections;
using UnityEngine;

public class PanInteraction : MonoBehaviour, IServePlate, IHeldPrimaryAction, IHeldObjectReceiver
{
    [SerializeField] private CatchableObj catchable;
    [SerializeField] private Transform ingredientSlot;

    [Header("Toss Trigger")]
    private Collider ingredientTrigger;
    [Min(0f)] [SerializeField] private float triggerDisableDuration = 0.5f;
    
    [Header("Ingredient Toss")]
    [Min(0f)]
    [SerializeField] private float tossForce = 3f;
    [Min(0f)]
    [SerializeField] private float tossUpForce = 2f;

    private IngredientReaction currentIngredient;
    private Vector3 currentIngredientOriginalScale;
    private Coroutine grillCoroutine;
    private Coroutine triggerRestoreCoroutine;
    private StoveInteraction currentStove;

    public CatchableObj Catchable => catchable;
    public bool HasIngredient => currentIngredient != null;

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();

        if (ingredientTrigger == null)
        {
            foreach (Collider candidate in GetComponents<Collider>())
            {
                if (!candidate.isTrigger) continue;

                ingredientTrigger = candidate;
                break;
            }
        }
    }

    private void OnDisable()
    {
        RestoreIngredientTrigger();
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
        if (currentIngredient.IsActionCompleted(IngredientAction.Grill)) return;
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
        currentIngredientOriginalScale = ingredient.transform.localScale;

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

        IngredientReaction ingredient = currentIngredient;
        CatchableObj ingredientCatchable = ingredient.Catchable;
        StopGrill();
        currentIngredient = null;

        TemporarilyDisableIngredientTrigger();
        RestoreIngredientTransform(ingredient);
        ingredientCatchable.ChangePickState(true);
        ingredientCatchable.OnThrow();

        if (ingredientCatchable.Rb == null) return;

        Vector2 random = Random.insideUnitCircle.normalized;
        Vector3 direction = new Vector3(random.x, 0f, random.y);
        Vector3 impulse = direction * tossForce + Vector3.up * tossUpForce;
        ingredientCatchable.Rb.linearVelocity = Vector3.zero;
        ingredientCatchable.Rb.angularVelocity = Vector3.zero;
        ingredientCatchable.Rb.AddForce(impulse, ForceMode.Impulse);
    }

    private void ConsumeIngredient()
    {
        IngredientReaction ingredient = currentIngredient;
        StopGrill();
        currentIngredient = null;

        CatchableObj ingredientCatchable = ingredient.Catchable;
        RestoreIngredientTransform(ingredient);
        ingredientCatchable.ChangePickState(false);
        ingredientCatchable.SetPhysicsState(false);

        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.Push(ingredientCatchable.gameObject);
            return;
        }

        ingredientCatchable.gameObject.SetActive(false);
    }

    private void RestoreIngredientTransform(IngredientReaction ingredient)
    {
        ingredient.transform.SetParent(null, true);
        ingredient.transform.localScale = currentIngredientOriginalScale;
    }

    // ✨ 지정한 시간 동안 음식 감지 Trigger를 비활성화한다.
    private void TemporarilyDisableIngredientTrigger()
    {
        if (ingredientTrigger == null) return;

        if (triggerRestoreCoroutine != null)
            StopCoroutine(triggerRestoreCoroutine);

        ingredientTrigger.enabled = false;
        triggerRestoreCoroutine = StartCoroutine(RestoreIngredientTriggerRoutine());
    }

    // ✨ 음식이 팬에서 벗어날 시간을 준 뒤 Trigger를 다시 활성화한다.
    private IEnumerator RestoreIngredientTriggerRoutine()
    {
        yield return new WaitForSeconds(triggerDisableDuration);
        ingredientTrigger.enabled = true;
        triggerRestoreCoroutine = null;
    }

    // ✨ 팬 비활성화 시에도 Trigger와 Coroutine 상태를 정상화한다.
    private void RestoreIngredientTrigger()
    {
        if (triggerRestoreCoroutine != null)
        {
            StopCoroutine(triggerRestoreCoroutine);
            triggerRestoreCoroutine = null;
        }

        if (ingredientTrigger != null)
            ingredientTrigger.enabled = true;
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
