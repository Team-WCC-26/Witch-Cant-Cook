using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;
using UnityEngine.Serialization;
using TMPro;
using UnityEngine.UI;

public class DishOrderManager : MonoBehaviour
{

    [Tooltip("All scrolls show the same first three active orders, aligned to the top.")]
    [FormerlySerializedAs("scrollSlots")]
    [SerializeField] private RecipeScrollView[] scrollViews = Array.Empty<RecipeScrollView>();

    [Header("Local Preview (Play Mode context menu)")]
    [SerializeField] private int previewRecipeId = 1;

    [Header("Submission Feedback")]
    [SerializeField] private TMP_FontAsset successFont;
    [SerializeField, Min(0.1f)] private float successDisplaySeconds = 2f;
    private GameObject successCanvas;
    private TMP_Text submissionResultText;
    private float successHideAt;

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
        {
            DishSubmissionVfx.PlayForServerResult(packet.State);
            ApplyState(packet.RecipeId, packet.State);
        }

        RefreshViews();
        if (successCanvas != null && Time.unscaledTime >= successHideAt)
            successCanvas.SetActive(false);
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
                // The protocol does not distinguish submission failure from order timeout.
                string resultName = ResolveRecipe(recipeId)?.name ?? "이름 미확인";
                ShowSubmissionMessage(state == DishState.Success ? "제출 성공!" : "주문 시간 초과 / 실패",
                    $"서버 결과: RecipeId={recipeId} / {resultName}\nState={state}\n현재 패킷에는 제출 EntityId와 실패 사유가 없습니다.",
                    state == DishState.Success ? Color.green : new Color(1f, 0.4f, 0.4f), 6f);
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
                name = ingredient != null ? ingredient.name : $"Ingredient #{row.ingID}",
                iconName = ingredient != null ? ingredient.IconName : null,
                conditionFlag = (IngredientState)row.conditionFlag,
                amount = row.amount
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

        while (receivedPackets.TryDequeue(out _)) { }
        orders.Clear();
        recipeCards.Clear();
        ingredientIndex = null;
        missingRecipes.Clear();

        foreach (RecipeScrollView view in views)
            if (view != null) view.Clear();
        RecipeSpriteLookup.ReleaseAll();
        if (successCanvas != null) successCanvas.SetActive(false);
    }

    public static void ReportSubmission(PlateInteraction plate, long plateId)
    {
        if (dishOrderManager == null) return;
        dishOrderManager.ShowSubmissionDetails(plate, plateId);
    }

    public static void ReportSubmissionIssue(string reason)
    {
        if (dishOrderManager != null)
            dishOrderManager.ShowSubmissionMessage("제출 요청 확인 필요", reason, Color.yellow, 8f);
    }

    private void ShowSubmissionDetails(PlateInteraction plate, long plateId)
    {
        CatchableObj food = plate.CurrentFood;
        CatchableData foodData = food != null ? food.Data : null;
        if (dataManager == null) dataManager = FindFirstObjectByType<DataManager>();
        var candidates = new List<Recipe>();
        FoodRecipeIdentity identity = food != null ? food.GetComponent<FoodRecipeIdentity>() : null;
        int submittedRecipeId = identity != null ? identity.RecipeId : 0;
        if (submittedRecipeId > 0 && dataManager != null && dataManager.IsDataLoaded &&
            dataManager.GetRecipe().TryGet(submittedRecipeId, out Recipe submittedRecipe))
            candidates.Add(submittedRecipe);

        string recipeInfo = candidates.Count == 0 ? "매핑 없음 (음식 ID와 레시피 ID는 별개)" :
            string.Join(", ", candidates.ConvertAll(recipe => $"{recipe.id} / {recipe.name}"));
        var active = new List<DishOrder>();
        orders.GetVisible(Time.unscaledTimeAsDouble, int.MaxValue, active);
        bool matches = active.Exists(order => order.RecipeId == submittedRecipeId);
        string comparison = submittedRecipeId <= 0 ? "음식에 RecipeId 없음: 일반 재료이거나 생성 정보 누락" :
            matches ? "현재 주문에 같은 레시피 있음 (서버 성공 확정 아님)" : "현재 유효 주문에 같은 레시피 없음";
        IngredientAction actions = IngredientAction.None;
        if (food != null && food.TryGetComponent(out IngredientReaction reaction))
            foreach (IngredientAction action in new[] { IngredientAction.Cut, IngredientAction.Grill, IngredientAction.Boil, IngredientAction.Cook })
                if (reaction.IsActionCompleted(action)) actions |= action;
        string orderInfo = active.Count == 0 ? "없음" : string.Join(", ", active.ConvertAll(order =>
            $"{order.RecipeId} / {ResolveRecipe(order.RecipeId)?.name ?? "이름 미확인"}"));
        string details = $"접시 EntityId={plateId} / 음식 EntityId={food?.NetworkId}\n" +
            $"음식 데이터 ID={foodData?.id} / {foodData?.name ?? "미확인"}\n" +
            $"음식 RecipeId={submittedRecipeId} / {recipeInfo}\n" +
            $"로컬 조리 상태: {actions} ({(byte)actions})\n" +
            $"주문 비교: {comparison}\n현재 주문: {orderInfo}\n" +
            "서버 판정 대기 · 응답이 없어도 실패로 확정하지 않습니다.";
        ShowSubmissionMessage("제출 요청", details, Color.white, 12f);
    }

    private void ShowSubmissionResult(bool succeeded) => ShowSubmissionMessage(
        succeeded ? "제출 성공!" : "제출 실패!", "UI 미리보기", succeeded ? Color.green : Color.red, successDisplaySeconds);

    private void ShowSubmissionMessage(string title, string details, Color color, float duration)
    {
        if (successCanvas == null)
        {
            successCanvas = new GameObject("Submission Success Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            // Keep overlay UI at the scene root, independent of world canvases and CanvasGroups.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(successCanvas, gameObject.scene);
            Canvas canvas = successCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = successCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var labelObject = new GameObject("Success Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(successCanvas.transform, false);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            submissionResultText = label;
            // Reuse the recipe UI font so Korean glyphs use the project's existing font asset.
            TMP_FontAsset font = successFont;
            foreach (RecipeScrollView view in views)
            {
                if (font != null) break;
                if (view == null) continue;
                TMP_Text existing = view.GetComponentInChildren<TMP_Text>(true);
                if (existing != null) font = existing.font;
            }
            if (font != null) label.font = font;
            label.fontSize = 50;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1600, 850);
        }
        submissionResultText.text = $"{title}\n<size=26>{details}</size>";
        submissionResultText.color = color;
        successHideAt = Time.unscaledTime + Mathf.Max(0.1f, duration);
        successCanvas.SetActive(true);
        Debug.Log($"<color=#87CEFA>[Submit UI] Showing result: {submissionResultText.text}</color>", this);
    }

    private void OnDestroy()
    {
        if (successCanvas != null) Destroy(successCanvas);
    }

    [ContextMenu("Preview/Show Submission Success (Play Mode)")]
    private void PreviewSubmissionSuccess()
    {
        if (Application.isPlaying && isActiveAndEnabled) ShowSubmissionResult(true);
    }

    [ContextMenu("Preview/Show Submission Failure (Play Mode)")]
    private void PreviewSubmissionFailure()
    {
        if (Application.isPlaying && isActiveAndEnabled) ShowSubmissionResult(false);
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
