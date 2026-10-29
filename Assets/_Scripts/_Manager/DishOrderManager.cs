using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;
using UnityEngine.Serialization;

public class DishOrderManager : MonoBehaviour
{

    [Tooltip("All scrolls show the same first three active orders, aligned to the top.")]
    [FormerlySerializedAs("scrollSlots")]
    [SerializeField] private RecipeScrollView[] scrollViews = Array.Empty<RecipeScrollView>();

    [Header("Local Preview (Play Mode context menu)")]
    [SerializeField] private int previewRecipeId = 1;

    private RoomHudSession moneySession;
    private SubmissionRewardPopup rewardPopup;
    private readonly ConcurrentQueue<int> successfulRecipes = new();
    private readonly ConcurrentQueue<int> earnedRewards = new();
    private bool stagePresentationPaused;

    public void SetStagePresentationPaused(bool paused)
    {
        stagePresentationPaused = paused;
        ClearSubmissionFeedback();
        if (!paused) return;
        while (receivedPackets.TryDequeue(out _)) { }
        orders.Clear();
        visibleOrders.Clear();
        foreach (RecipeScrollView view in views) if (view != null) view.Clear();
    }

    [Header("Runtime Status (read only while playing)")]
    [SerializeField] private string lastPacket;
    [SerializeField] private int pendingOrders;
    [SerializeField] private int displayedOrders;

    private static DishOrderManager dishOrderManager;

    private ServerManager registeredServer;
    private DataManager dataManager;
    private RecipeIngredientIndex ingredientIndex;
    private readonly Dictionary<int, RecipeCardData> recipeCards = new();
    private readonly HashSet<int> missingRecipes = new();

    private readonly DishOrderBook orders = new();
    private readonly List<DishOrder> visibleOrders = new();
    private readonly List<RecipeScrollView> views = new();
    private readonly ConcurrentQueue<DishStatePacket> receivedPackets = new();

    public int PendingOrderCount => orders.Count;

    private void OnEnable()
    {
        if (dishOrderManager != null && dishOrderManager != this)
        {
            Debug.LogError("Only one DishOrderManager may receive S_DishState at a time.", this);
            enabled = false;
            return;
        }

        dishOrderManager = this;
        moneySession = RoomHudSession.Instance;
        moneySession.PendingMoneyIncreased += OnPendingMoneyIncreased;
        moneySession.RoomJoined += ClearSubmissionFeedback;
        views.Clear();

        foreach (RecipeScrollView view in scrollViews)
        {
            if (view == null || views.Contains(view)) continue;
            views.Add(view);
            view.Clear();
        }

        if (views.Count == 0)
            Debug.LogWarning("DishOrderManager: no scroll views assigned.", this);

        if (StageReturnFlow.IsSettlementActive) SetStagePresentationPaused(true);
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
        DishStatePacket packet = PacketSerializer.Deserialize<DishStatePacket>(data);
        // Capture before the presentation queue: S_StageResult may arrive in the same frame.
        if (packet.State == DishState.Success && !stagePresentationPaused &&
            moneySession != null && moneySession.State.RecordSuccessfulDish())
            successfulRecipes.Enqueue(packet.RecipeId);
        receivedPackets.Enqueue(packet);
    }

    private void Update()
    {
        TryRegister();

        // Always update Unity objects on the main thread, regardless of the dispatcher's caller.
        if (stagePresentationPaused) return;
        while (receivedPackets.TryDequeue(out DishStatePacket packet))
        {
            DishSubmissionVfx.PlayForServerResult(packet.State);
            ApplyState(packet.RecipeId, packet.State);
        }

        RefreshViews();
        PresentSubmissionRewards();
    }

    private bool ApplyState(int recipeId, DishState state)
    {
        lastPacket = $"{state} / RecipeId {recipeId} / {Time.unscaledTimeAsDouble:F3}s";
        switch (state)
        {
            case DishState.Order:
                RecipeCardData data = ResolveRecipe(recipeId);
                orders.Add(recipeId, Time.unscaledTimeAsDouble, data != null ? data.timeLimit : 0);
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
        for (int i = 0; i < orders.Count; i++)
        {
            if (orders[i].DurationSeconds <= 0)
                orders[i].SetDurationIfUnknown(ResolveRecipe(orders[i].RecipeId)?.timeLimit ?? 0);
        }

        orders.GetVisible(now, RecipeScrollView.Capacity, visibleOrders);

        pendingOrders = orders.Count;
        displayedOrders = visibleOrders.Count;

        foreach (RecipeScrollView view in views)
            if (view != null) view.Show(visibleOrders, now, ResolveRecipe);
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
            iconName = recipe.IconName
        };

        if (ingredientIndex == null)
        {
            var rows = dataManager.GetIngredientCombination().GetAll();
            if (rows.Count == 0)
                Debug.LogWarning("IngredientCombination has no loaded rows. Check GSpreadReader Sheets and the JSON cache.", this);
            ingredientIndex = new RecipeIngredientIndex(rows);
        }
        foreach (IngredientCombination row in ingredientIndex.GetRows(recipeId))
        {
            if (row == null || row.recipeID != recipeId) continue;

            if (!dataManager.GetIngredient().TryGet(row.ingID, out Ingredient ingredient))
                Debug.LogWarning($"Recipe {recipeId} references missing Ingredient {row.ingID}.", this);

            card.ingredients.Add(new RecipeIngredientDisplayData
            {
                iconName = ingredient != null ? ingredient.IconName : null,
                conditionFlag = (IngredientState)row.conditionFlag
            });

        }

        recipeCards.Add(recipeId, card);
        return card;
    }

    private void OnDisable()
    {
        if (dishOrderManager != this) return;
        if (registeredServer != null) registeredServer.UnRegisterHandler(PacketId.S_DishState);

        registeredServer = null;
        dishOrderManager = null;
        if (moneySession != null)
        {
            moneySession.PendingMoneyIncreased -= OnPendingMoneyIncreased;
            moneySession.RoomJoined -= ClearSubmissionFeedback;
        }
        ClearSubmissionFeedback();
        moneySession = null;

        while (receivedPackets.TryDequeue(out _)) { }
        orders.Clear();
        recipeCards.Clear();
        ingredientIndex = null;
        missingRecipes.Clear();

        foreach (RecipeScrollView view in views)
            if (view != null) view.Clear();
    }

    public static void ReportSubmission(PlateInteraction plate, long plateId)
    {
        Debug.Log($"[Submit Request] EntityId={plateId}");
    }

    public static void ReportSubmissionIssue(string reason) => Debug.LogWarning($"[Submit Issue] {reason}");

    private void OnPendingMoneyIncreased(int earned, int total)
    {
        if (!stagePresentationPaused) earnedRewards.Enqueue(earned);
    }

    private void ClearSubmissionFeedback()
    {
        while (successfulRecipes.TryDequeue(out _)) { }
        while (earnedRewards.TryDequeue(out _)) { }
        if (rewardPopup != null) rewardPopup.Clear();
    }

    private void PresentSubmissionRewards()
    {
        if (successfulRecipes.IsEmpty || earnedRewards.IsEmpty) return;
        if (dataManager == null) dataManager = FindFirstObjectByType<DataManager>();
        if (dataManager == null || !dataManager.IsDataLoaded) return;
        if (rewardPopup == null) rewardPopup = FindFirstObjectByType<SubmissionRewardPopup>();
        if (rewardPopup == null) return;
        // Each rewarded submission has one success packet and one increased pending snapshot.
        // Pair the two ordered streams even when their callbacks arrive in different frames.
        while (successfulRecipes.TryPeek(out _) && earnedRewards.TryPeek(out _))
        {
            if (!successfulRecipes.TryDequeue(out int recipeId) || !earnedRewards.TryDequeue(out int earned)) break;
            rewardPopup.Enqueue(ResolveRecipe(recipeId)?.iconName, earned);
        }
    }

    [ContextMenu("Preview/Show Submission Success (Play Mode)")]
    private void PreviewSubmissionSuccess()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        SubmissionRewardPopup popup = FindFirstObjectByType<SubmissionRewardPopup>();
        if (popup != null) popup.Enqueue(ResolveRecipe(previewRecipeId)?.iconName, 100);
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

