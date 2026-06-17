using Protocol;
using Server;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PotInteraction : MonoBehaviour
{
    [SerializeField] private PotVisualController visualController;
    [SerializeField] private InteractionGaugeUI gaugeUI;
    [SerializeField] private float cookDuration = 2f;

    private static readonly List<PotInteraction> activePots = new();
    private static bool isCombineHandlerRegistered;

    private bool hasIngredient;
    private bool isDone;
    private bool isWaitingCombine;
    private int currentIngredientId;
    private long currentEntityId;
    private long cookStartedEntityId;
    private long pendingSubjectEntityId;
    private long pendingTargetEntityId;

    private IEnumerator Start()
    {
        yield return new WaitUntil(() => ServerManager.Instance != null);
        TryRegisterCombineHandler();
    }

    private void OnEnable()
    {
        activePots.Add(this);
        IngredientNetworkBridge.CookCompleted += OnCookComplete;

        if (ServerManager.Instance != null)
            TryRegisterCombineHandler();

        gaugeUI.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        activePots.Remove(this);
        IngredientNetworkBridge.CookCompleted -= OnCookComplete;
        TryUnregisterCombineHandler();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isDone) return;
        if (isWaitingCombine) return;

        if (!TryGetIngredient(other, out CatchableObj catchable, out int ingredientId))
            return;

        if (!hasIngredient)
        {
            ApplyFirstIngredient(catchable, ingredientId);
            ConsumeIngredient(catchable);
            return;
        }

        if (RequestCombine(catchable))
            ConsumeIngredient(catchable);
    }

    private void OnCookComplete(CookCompletePacket packet)
    {
        if (!hasIngredient) return;
        if (isDone) return;
        if (packet.CookType != IngredientState.Boiled) return;
        if (packet.EntityId != cookStartedEntityId && packet.EntityId != currentEntityId) return;

        isDone = true;
        isWaitingCombine = false;
        currentIngredientId = packet.IngredientId;

        gaugeUI.gameObject.SetActive(false);
        visualController.UpdateVisual(currentIngredientId, true);
    }

    #region Combine Handler
    private static void TryRegisterCombineHandler()
    {
        if (isCombineHandlerRegistered) return;
        if (ServerManager.Instance == null) return;

        ServerManager.Instance.RegisterHandler(PacketId.S_EntityCombine, OnEntityCombine);
        isCombineHandlerRegistered = true;
    }

    private static void TryUnregisterCombineHandler()
    {
        if (!isCombineHandlerRegistered) return;
        if (activePots.Count > 0) return;
        if (ServerManager.Instance == null) return;

        ServerManager.Instance.UnRegisterHandler(PacketId.S_EntityCombine);
        isCombineHandlerRegistered = false;
    }

    private static void OnEntityCombine(ReadOnlyMemory<byte> data)
    {
        EntityCombineResultPacket packet = PacketSerializer.Deserialize<EntityCombineResultPacket>(data.Span);

        foreach (PotInteraction pot in activePots)
        {
            if (pot.TryApplyCombineResult(packet))
                return;
        }
    }

    private bool TryApplyCombineResult(EntityCombineResultPacket packet)
    {
        if (!isWaitingCombine) return false;
        if (packet.SubjectEntityId != pendingSubjectEntityId) return false;
        if (packet.TargetEntityId != pendingTargetEntityId) return false;

        isWaitingCombine = false;
        pendingSubjectEntityId = 0;
        pendingTargetEntityId = 0;

        if (!packet.Success)
            return true;

        currentEntityId = packet.RemainingEntityId;
        currentIngredientId = packet.ResultIngredientId;
        hasIngredient = true;

        visualController.UpdateVisual(currentIngredientId);
        return true;
    }
    #endregion

    private void ApplyFirstIngredient(CatchableObj catchable, int ingredientId)
    {
        hasIngredient = true;
        currentEntityId = catchable.NetworkId;
        currentIngredientId = ingredientId;
        cookStartedEntityId = catchable.NetworkId;

        visualController.UpdateVisual(currentIngredientId);
        gaugeUI.gameObject.SetActive(true);
        gaugeUI?.StartFill(cookDuration);
        IngredientNetworkBridge.RequestCookStart(cookStartedEntityId, IngredientState.Boiled);
    }

    private bool RequestCombine(CatchableObj catchable)
    {
        if (ServerManager.Instance == null)
        {
            Debug.LogWarning("Cannot request pot combine because ServerManager.Instance is null.");
            return false;
        }

        pendingSubjectEntityId = catchable.NetworkId;
        pendingTargetEntityId = currentEntityId;
        isWaitingCombine = true;

        EntityCombinePacket packet = new()
        {
            SubjectEntityId = pendingSubjectEntityId,
            TargetEntityId = pendingTargetEntityId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
        return true;
    }

    private bool TryGetIngredient(Collider other, out CatchableObj catchable, out int ingredientId)
    {
        catchable = null;
        ingredientId = 0;

        IngredientReaction reaction = other.GetComponentInParent<IngredientReaction>();
        if (reaction == null) return false;

        catchable = reaction.Catchable;
        if (catchable == null) return false;
        if (catchable.Data is not Ingredient ingredient) return false;

        ingredientId = ingredient.id;
        return true;
    }

    private void ConsumeIngredient(CatchableObj catchable)
    {
        catchable.ChangePickState(false);
        catchable.SetPhysicsState(false);

        if (ObjectPoolManager.Instance != null)
        {
            ObjectPoolManager.Instance.Push(catchable.gameObject);
            return;
        }

        catchable.gameObject.SetActive(false);
    }
}
