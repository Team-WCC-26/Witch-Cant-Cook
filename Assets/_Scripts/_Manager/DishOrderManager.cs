using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;
using UnityEngine.Serialization;

public class DishOrderManager : MonoBehaviour
{
    [SerializeField] private RecipeVisualCatalog catalog;
    [Tooltip("Order-flow test: show names only and wait for server results without local expiration.")]
    [SerializeField] private bool namesOnly = true;
    [Tooltip("All scrolls show the same first three active orders, aligned to the top.")]
    [FormerlySerializedAs("scrollSlots")]
    [SerializeField] private RecipeScrollView[] scrollViews = Array.Empty<RecipeScrollView>();

    [Header("Local Preview (Play Mode context menu)")]
    [SerializeField] private int previewRecipeId = 1;

    [Header("Runtime Status (read only while playing)")]
    [SerializeField] private string lastPacket;
    [SerializeField] private int pendingOrders;
    [SerializeField] private int displayedOrders;

    private static DishOrderManager activeManager;

    private ServerManager registeredServer;
    private DataManager dataManager;
    private readonly Dictionary<int, RecipeCardData> recipeCards = new();
    private readonly HashSet<int> missingRecipes = new();

    private readonly DishOrderBook orders = new();
    private readonly List<DishOrder> visibleOrders = new();
    private readonly List<RecipeScrollView> views = new();
    private readonly ConcurrentQueue<DishStatePacket> receivedPackets = new();

    public int PendingOrderCount => orders.Count;

    private void OnEnable()
    {
        if (activeManager != null && activeManager != this)
        {
            Debug.LogError("Only one DishOrderManager may receive S_DishState at a time.", this);
            enabled = false;
            return;
        }

        activeManager = this;
        views.Clear();

        foreach (RecipeScrollView view in scrollViews)
        {
            if (view == null || views.Contains(view)) continue;
            views.Add(view);
            view.Clear();
        }

        if (views.Count == 0)
            Debug.LogWarning("DishOrderManager: no scroll views assigned.", this);

        RefreshViews();
    }

    private void Start() => TryRegister();

    private void TryRegister()
    {
        if (registeredServer != null) return;

        // Start/Update retry allows ServerManager to be created after this scene object.
        registeredServer = FindFirstObjectByType<ServerManager>();

        if (registeredServer != null)
            registeredServer.RegisterHandler(PacketId.S_DishState, ReceivePacket);
    }

    private void ReceivePacket(ReadOnlyMemory<byte> data)
    {
        receivedPackets.Enqueue(PacketSerializer.Deserialize<DishStatePacket>(data));
    }

    private void Update()
    {
        TryRegister();


        // Always update Unity objects on the main thread, regardless of the dispatcher's caller.
        while (receivedPackets.TryDequeue(out DishStatePacket packet))
            ApplyState(packet.RecipeId, packet.State);

        RefreshViews();
    }

    private bool ApplyState(int recipeId, DishState state)
    {
        lastPacket = $"{state} / RecipeId {recipeId} / {Time.unscaledTimeAsDouble:F3}s";
        switch (state)
        {
            case DishState.Order:
                RecipeCardData data = ResolveRecipe(recipeId);
                orders.Add(recipeId, Time.unscaledTimeAsDouble, !namesOnly && data != null ? data.timeLimit : 0);
                Debug.Log($"DishOrderManager: received {state}  RecipeId {recipeId}.", this);

                return true;

            case DishState.Success:

            case DishState.Fail:
                for (int i = 0; i < orders.Count; i++)
                {
                    if (orders[i].RecipeId != recipeId) continue;
                    double elapsed = Time.unscaledTimeAsDouble - orders[i].ReceivedAt;
                    orders.Complete(recipeId);
                    Debug.Log($"DishOrderManager: received {state}, RecipeId {recipeId}; removed after {elapsed:F3}s.", this);
                    return true;
                }
                Debug.LogWarning($"DishOrderManager: received {state} for unknown RecipeId {recipeId}.", this);
                return false;

            default:
                return false;
        }
    }

    private void RefreshViews()
    {
        double now = Time.unscaledTimeAsDouble;
        if (!namesOnly)
            for (int i = 0; i < orders.Count; i++)
                if (orders[i].DurationSeconds <= 0)
                    orders[i].SetDurationIfUnknown(ResolveRecipe(orders[i].RecipeId)?.timeLimit ?? 0);
        orders.GetVisible(now, RecipeScrollView.Capacity, visibleOrders);
        pendingOrders = orders.Count;
        displayedOrders = visibleOrders.Count;
        foreach (RecipeScrollView view in views)
            if (view != null) view.Show(visibleOrders, catalog, now, namesOnly, ResolveRecipe);
    }

    private RecipeCardData ResolveRecipe(int recipeId)
    {
        if (recipeCards.TryGetValue(recipeId, out RecipeCardData cached)) return cached;
        if (dataManager == null) dataManager = FindFirstObjectByType<DataManager>();
        if (dataManager == null || !dataManager.IsDataLoaded) return null;
        if (!dataManager.GetRecipe().TryGet(recipeId, out Recipe recipe))
        {
            if (missingRecipes.Add(recipeId))
                Debug.LogWarning($"Recipe sheet has no row for RecipeId {recipeId}.", this);
            return null;
        }
        RecipeCardData card = new()
        {
            recipeId = recipe.id,
            name = recipe.name,
            timeLimit = recipe.timeLimit,
            sprite = catalog != null ? catalog.GetRecipeSprite(recipeId) : null
        };
        recipeCards.Add(recipeId, card);
        return card;
    }

    private void OnDisable()
    {
        if (activeManager != this) return;
        if (registeredServer != null) registeredServer.UnRegisterHandler(PacketId.S_DishState);

        registeredServer = null;
        activeManager = null;

        while (receivedPackets.TryDequeue(out _)) { }
        orders.Clear();
        recipeCards.Clear();
        missingRecipes.Clear();

        foreach (RecipeScrollView view in views)
            if (view != null) view.Clear();
    }

    [ContextMenu("Preview/Add Order (Play Mode)")]
    private void PreviewOrder() => PreviewState(DishState.Order);

    [ContextMenu("Preview/Complete Order (Play Mode)")]
    private void PreviewSuccess() => PreviewState(DishState.Success);

    [ContextMenu("Preview/Fail Order (Play Mode)")]
    private void PreviewFail() => PreviewState(DishState.Fail);

    [ContextMenu("Debug/Report UI State (Play Mode)")]
    private void ReportUIState()
    {
        Debug.Log($"DishOrderManager: last={lastPacket}, pending={orders.Count}, visible={visibleOrders.Count}, views={views.Count}", this);
        foreach (RecipeScrollView view in views)
            if (view != null) view.ReportUIState();
    }

    private void PreviewState(DishState state)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        if (ApplyState(previewRecipeId, state)) RefreshViews();
    }
}
