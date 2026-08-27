using Protocol;
using Server;
using System.Collections.Generic;
using UnityEngine;

public class PotInteraction : MapObjInteraction,
    IServePlate,
    IEntityParentReceiver,
    IObjCombineReceiver,
    ICookReceiver
{
    private const int TrashIngredientId = 99999;

    [SerializeField] private PotVisualController visualController;
    [SerializeField] private InteractionGaugeUI gaugeUI;

    private readonly HashSet<long> insertedEntities = new();
    private long currentEntityId;

    private void OnEnable()
    {
        gaugeUI?.Hide();
        visualController?.HideAll();
        insertedEntities.Clear();
        currentEntityId = 0;
    }

    private void OnDisable()
    {
        gaugeUI?.Hide();
        visualController?.HideAll();
        insertedEntities.Clear();
        currentEntityId = 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsRegistered || ServerManager.Instance == null) return;
        if (!other.TryGetComponent(out IngredientReaction reaction)) return;

        CatchableObj catchable = reaction.Catchable;
        if (catchable == null || catchable.NetworkId == 0) return;
        if (!insertedEntities.Add(catchable.NetworkId)) return;

        // Insert request
        EntityInsertPacket packet = new()
        {
            SubjectEntityId = catchable.NetworkId,
            TargetEntityId = NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.TryGetComponent(out IngredientReaction reaction)) return;
        if (reaction.Catchable == null) return;

        insertedEntities.Remove(reaction.Catchable.NetworkId);
    }

    public void HandleEntityAdded(CatchableObj entity)
    {
        // Parent result
        if (entity == null || entity.Data is not Ingredient) return;

        currentEntityId = entity.NetworkId;
        entity.ChangePickState(false);
        entity.SetPhysicsState(false);
        visualController?.ShowPrimary(entity);
    }

    public void HandleEntityRemoved(CatchableObj entity)
    {
        if (entity == null || entity.NetworkId != currentEntityId) return;

        entity.transform.SetParent(null, true);
        currentEntityId = 0;
        gaugeUI?.Hide();
        visualController?.HideAll();
    }

    public void HandleIngredientCombine(
        IngredientCombinePacket packet,
        CatchableObj result,
        CatchableObj secondaryVisual)
    {
        // Combine result
        currentEntityId = packet.NewEntityId;
        result.ParentEntityId = NetworkId;

        insertedEntities.Remove(packet.SubjectEntityId);
        insertedEntities.Remove(packet.TargetEntityId);
        insertedEntities.Add(packet.NewEntityId);

        if (packet.ResultIngredientId == TrashIngredientId)
            visualController?.ShowPrimary(result);
        else
            visualController?.ShowCombined(result, secondaryVisual);
    }

    public void HandleCookStart(CookStartPacket packet)
    {
        // Tool gauge
        gaugeUI?.StartFill(packet.CookingTimeMs / 1000f);
    }

    public void HandleCookPause(CookPausePacket packet)
    {
        gaugeUI?.StopFill();
    }

    public void HandleCookComplete(CookCompletePacket packet)
    {
        if (packet.IngredientEntityId != currentEntityId) return;
        if ((packet.CookType & IngredientState.Boiled) == 0) return;

        gaugeUI?.Hide();
        visualController?.ApplyCookedVisual();
    }

    public bool TryServePlate(PlateInteraction plate)
    {
        if (plate == null || currentEntityId == 0) return false;
        if (ServerManager.Instance == null) return false;

        // Server transfer
        EntityInteractPacket packet = new() { TargetEntityId = NetworkId };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
        return true;
    }
}
