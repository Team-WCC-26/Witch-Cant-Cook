using Protocol;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OvenInteraction : MapObjInteraction
{
    [SerializeField] private float cookDuration = 2f;

    private readonly Dictionary<long, OvenCookState> currentIngredients = new();

    private void OnEnable()
    {
        IngredientNetworkBridge.CookCompleted += OnCookComplete;
    }

    private void OnDisable()
    {
        IngredientNetworkBridge.CookCompleted -= OnCookComplete;
        StopAllCooking();
    }

    private void OnDestroy()
    {
        StopAllCooking();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!TryGetIngredient(other, out IngredientReaction reaction, out CatchableObj catchable)) return;
        if (reaction.IsActionCompleted(IngredientAction.Cook)) return;

        long id = catchable.NetworkId;
        if (currentIngredients.ContainsKey(id)) return;

        Coroutine coroutine = StartCoroutine(CookIngredient(reaction, catchable));
        currentIngredients[id] = new OvenCookState(reaction, catchable.NetworkId, coroutine);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryGetIngredient(other, out IngredientReaction reaction, out CatchableObj catchable)) return;

        long id = catchable.NetworkId;
        if (!currentIngredients.TryGetValue(id, out OvenCookState state)) return;

        StopCooking(state);
        currentIngredients.Remove(id);
    }

    private IEnumerator CookIngredient(IngredientReaction reaction, CatchableObj catchable)
    {
        reaction.GaugeUI?.StartFill(cookDuration);
        IngredientNetworkBridge.RequestCookStart(catchable.NetworkId, IngredientState.Roasted);

        yield return new WaitForSeconds(cookDuration);
    }

    private void OnCookComplete(CookCompletePacket packet)
    {
        if (packet.CookType != IngredientState.Roasted) return;
        if (!currentIngredients.TryGetValue(packet.EntityId, out OvenCookState state)) return;

        state.Reaction.GaugeUI?.Hide();
        state.Reaction.Interact(IngredientAction.Cook, int.MaxValue);
        currentIngredients.Remove(packet.EntityId);
    }

    private bool TryGetIngredient(Collider other, out IngredientReaction reaction, out CatchableObj catchable)
    {
        reaction = other.GetComponentInParent<IngredientReaction>();
        catchable = null;

        if (reaction == null) return false;

        catchable = reaction.Catchable;
        if (catchable == null) return false;
        if (catchable.ObjType != CatchableObjType.Ingredient) return false;

        return true;
    }

    private void StopAllCooking()
    {
        foreach (OvenCookState state in currentIngredients.Values)
        {
            StopCooking(state);
        }

        currentIngredients.Clear();
    }

    private void StopCooking(OvenCookState state)
    {
        if (state.Coroutine != null)
            StopCoroutine(state.Coroutine);

        state.Reaction.GaugeUI?.Hide();
    }

    private readonly struct OvenCookState
    {
        public readonly IngredientReaction Reaction;
        public readonly long EntityId;
        public readonly Coroutine Coroutine;

        public OvenCookState(IngredientReaction reaction, long entityId, Coroutine coroutine)
        {
            Reaction = reaction;
            EntityId = entityId;
            Coroutine = coroutine;
        }
    }
}
