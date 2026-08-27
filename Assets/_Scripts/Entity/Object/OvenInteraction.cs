using Protocol;
using Server;
using System.Collections.Generic;
using UnityEngine;

public class OvenInteraction : MapObjInteraction,
    IEntityParentReceiver,
    ICookReceiver,
    IServePlate
{
    private readonly Dictionary<long, IngredientReaction> currentIngredients = new();
    private readonly HashSet<long> pendingEntities = new();

    private void OnDisable()
    {
        HideAllGauges();
        currentIngredients.Clear();
        pendingEntities.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsRegistered || ServerManager.Instance == null) return;
        if (!TryGetIngredient(other, out CatchableObj catchable)) return;
        if (catchable.NetworkId == 0) return;
        if (!pendingEntities.Add(catchable.NetworkId)) return;

        // Insert only
        EntityInsertPacket packet = new()
        {
            SubjectEntityId = catchable.NetworkId,
            TargetEntityId = NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void OnTriggerExit(Collider other)
    {
        if (!TryGetIngredient(other, out CatchableObj catchable)) return;
        pendingEntities.Remove(catchable.NetworkId);
    }

    public void HandleEntityAdded(CatchableObj entity)
    {
        // Parent result
        if (entity == null || !entity.TryGetComponent(out IngredientReaction reaction)) return;

        pendingEntities.Remove(entity.NetworkId);
        currentIngredients[entity.NetworkId] = reaction;
        entity.ChangePickState(false);
        entity.SetPhysicsState(false);
    }

    public void HandleEntityRemoved(CatchableObj entity)
    {
        if (entity == null) return;
        if (!currentIngredients.Remove(entity.NetworkId, out IngredientReaction reaction)) return;

        reaction.GaugeUI?.Hide();
    }

    public void HandleCookStart(CookStartPacket packet)
    {
        // Server start
        float duration = packet.CookingTimeMs / 1000f;
        foreach (IngredientReaction reaction in currentIngredients.Values)
            reaction.GaugeUI?.StartFill(duration);
    }

    public void HandleCookPause(CookPausePacket packet)
    {
        HideAllGauges();
    }

    public void HandleCookComplete(CookCompletePacket packet)
    {
        if ((packet.CookType & IngredientState.Roasted) == 0) return;
        if (!currentIngredients.TryGetValue(packet.IngredientEntityId, out IngredientReaction reaction)) return;

        reaction.GaugeUI?.Hide();
        reaction.ApplyServerAction(IngredientAction.Cook);
    }

    public bool TryServePlate(PlateInteraction plate)
    {
        if (plate == null || currentIngredients.Count == 0) return false;
        if (ServerManager.Instance == null) return false;

        // Server transfer
        EntityInteractPacket packet = new() { TargetEntityId = NetworkId };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
        return true;
    }

    private static bool TryGetIngredient(Collider other, out CatchableObj catchable)
    {
        IngredientReaction reaction = other.GetComponentInParent<IngredientReaction>();
        catchable = reaction != null ? reaction.Catchable : null;
        return catchable != null && catchable.ObjType == CatchableObjType.Ingredient;
    }

    private void HideAllGauges()
    {
        foreach (IngredientReaction reaction in currentIngredients.Values)
            reaction.GaugeUI?.Hide();
    }
}
