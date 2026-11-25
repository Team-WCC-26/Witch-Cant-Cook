using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Displays up to three orders using the existing recipe card views.
public class RecipeScrollView : MonoBehaviour
{
    [SerializeField] private RectTransform cardRoot;
    [SerializeField] private RecipeOrderCardUI[] cards = Array.Empty<RecipeOrderCardUI>();
    [SerializeField, Tooltip("Keep the layout components and spacing configured in the prefab.")]
    private bool usePrefabLayout;
    [Header("Order Animation")]
    [SerializeField, Min(0f), Tooltip("Seconds for existing orders to slide horizontally.")]
    private float horizontalSlideDuration = .12f;
    [SerializeField, Min(0f), Tooltip("Seconds to wait after the horizontal slide before the new order starts dropping.")]
    private float dropDelayAfterHorizontalSlide = 0f;
    [SerializeField, Min(0f), Tooltip("Seconds for a new order to slide down.")]
    private float orderSlideDuration = .22f;
    [Header("Recipe Details")]
    [SerializeField, Min(0f), Tooltip("Seconds to open or close the recipe details with Tab.")]
    private float detailsSlideDuration = .22f;
    public const int Capacity = 3;
    private StageReturnFlow stageFlow;
    private Canvas recipeCanvas;
    private CanvasGroup hudGroup;
    private bool canvasStateCached;
    private bool originalCanvasEnabled;
    private RecipeOrderSlideLayout orderSlides;

    private bool detailsExpanded;
    private float detailsProgress;

    private void Update()
    {
        float deltaTime = Time.unscaledDeltaTime;
        if (usePrefabLayout && Keyboard.current?.tabKey.wasPressedThisFrame == true &&
            !StageReturnFlow.IsInputBlocked && StageSelectionSession.Instance.IsStageRunning)
            detailsExpanded = !detailsExpanded;

        AdvanceDetails(deltaTime);
        orderSlides?.Advance(deltaTime);
    }

    private void AdvanceDetails(float deltaTime)
    {
        float target = detailsExpanded ? 1f : 0f;
        if (detailsProgress == target) return;

        float progress = detailsSlideDuration > 0f
            ? Mathf.MoveTowards(detailsProgress, target, deltaTime / detailsSlideDuration)
            : target;
        SetDetailsProgress(progress);
        RebuildPrefabLayout(animateOrders: false);
    }

    private void SetDetailsProgress(float progress)
    {
        if (detailsProgress == progress) return;

        detailsProgress = progress;
        float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
        foreach (RecipeOrderCardUI card in cards)
            if (card != null) card.SetDetailsProgress(easedProgress);
    }

    private void ResetDetails()
    {
        detailsExpanded = false;
        SetDetailsProgress(0f);
    }

    private void OnEnable()
    {
        if (!canvasStateCached)
        {
            recipeCanvas = GetComponent<Canvas>();
            hudGroup = GetComponent<CanvasGroup>();
            originalCanvasEnabled = recipeCanvas != null && recipeCanvas.enabled;
            canvasStateCached = true;
        }
        stageFlow = StageReturnFlow.Instance;
        stageFlow.Changed += RefreshCanvasVisibility;
        RefreshCanvasVisibility();
        if (DishOrderManager.Instance != null) DishOrderManager.Instance.RegisterView(this);
    }

    private void OnDisable()
    {
        ResetDetails();
        if (stageFlow != null) stageFlow.Changed -= RefreshCanvasVisibility;
        if (DishOrderManager.Instance != null) DishOrderManager.Instance.UnregisterView(this);
    }

    private void RefreshCanvasVisibility()
    {
        if (recipeCanvas != null) recipeCanvas.enabled = originalCanvasEnabled && !stageFlow.State.ShowsSettlement;
        if (hudGroup != null)
        {
            hudGroup.alpha = stageFlow.State.ShowsSettlement ? 0f : 1f;
            hudGroup.blocksRaycasts = !stageFlow.State.ShowsSettlement;
        }
    }

    public void Configure(RectTransform root, RecipeOrderCardUI[] views)
    {
        cardRoot = root;
        cards = views;
        ConfigureLayout();
    }
    private void Awake()
    {
        ResolveCards();
        ConfigureLayout();
        // Another object's OnEnable may already have bound an order before our Awake.
        foreach (RecipeOrderCardUI card in cards)
            if (card != null && card.OrderId < 0) card.Clear();
    }
    private void ResolveCards()
    {
        // Prefab replacement can leave the scene's card references empty.
        // Preserve explicit bindings; recover existing cards even when hidden.
        if (cardRoot == null || Array.Exists(cards, card => card != null)) return;

        cards = cardRoot.GetComponentsInChildren<RecipeOrderCardUI>(true);
    }
    private void ConfigureLayout()
    {
        if (cardRoot == null || usePrefabLayout) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
        {
            VerticalLayoutGroup oldLayout = cardRoot.GetComponent<VerticalLayoutGroup>();
            if (oldLayout != null) oldLayout.enabled = false;

            HorizontalLayoutGroup row = cardRoot.GetComponent<HorizontalLayoutGroup>();
            if (row == null) row = cardRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.UpperRight;
            row.spacing = 16f;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            return;
        }

        VerticalLayoutGroup layout = cardRoot.GetComponent<VerticalLayoutGroup>();

        if (layout == null) layout = cardRoot.gameObject.AddComponent<VerticalLayoutGroup>();

        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }
    public void Show(IReadOnlyList<DishOrder> orders, double now, Func<int, RecipeCardData> resolveRecipe = null)
    {
        ResolveCards();
        if (usePrefabLayout && cardRoot != null && cardRoot.gameObject.activeInHierarchy)
            orderSlides ??= new RecipeOrderSlideLayout(cardRoot, cards);

        bool layoutChanged = false;
        for (int i = 0; i < cards.Length; i++)
        {
            RecipeOrderCardUI card = cards[i];
            if (card == null) continue;

            if (i >= Capacity || i >= orders.Count)
            {
                if (card.gameObject.activeSelf)
                {
                    if (!layoutChanged) orderSlides?.CaptureVisiblePositions();
                    card.Clear();
                    layoutChanged = true;
                }
                continue;
            }

            DishOrder order = orders[i];
            RecipeCardData data = resolveRecipe?.Invoke(order.RecipeId);

            if (card.OrderId != order.Id || !ReferenceEquals(card.BoundData, data))
            {
                if (!layoutChanged) orderSlides?.CaptureVisiblePositions();
                card.Bind(order, data);
                layoutChanged = true;
            }

            card.SetRemaining(order, now);
        }

        if (layoutChanged) RebuildPrefabLayout();
    }

    private void RebuildPrefabLayout(bool animateOrders = true)
    {
        if (!usePrefabLayout || cardRoot == null || !cardRoot.gameObject.activeInHierarchy) return;

        // Each card fits its contents. The row must read those sizes before its first visible frame.
        for (int i = 0; i < cards.Length; i++)
        {
            RecipeOrderCardUI card = cards[i];
            bool visible = card != null && card.gameObject.activeSelf;
            orderSlides?.SetSlotVisible(i, visible);
            if (!visible || !card.gameObject.activeInHierarchy || card.transform is not RectTransform rect)
                continue;

            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            orderSlides?.FitSlotToCard(i);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(cardRoot);
        if (animateOrders)
            orderSlides?.BeginSlides(transform as RectTransform, horizontalSlideDuration, dropDelayAfterHorizontalSlide, orderSlideDuration);
    }

    public void Clear()
    {
        ResetDetails();
        // DishOrderManager.OnEnable can run before this view's Awake.
        ResolveCards();
        foreach (RecipeOrderCardUI card in cards) if (card != null) card.Clear();
        orderSlides?.Clear();
    }

    public void ReportUIState()
    {
        Canvas canvas = GetComponentInParent<Canvas>();

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) 
            { 
                Debug.LogWarning($"Card {i + 1}: missing reference", this);
                continue; 
            }

            RectTransform rect = cards[i].GetComponent<RectTransform>();
            Debug.Log($"Card {i + 1}: order={cards[i].OrderId}, activeSelf={cards[i].gameObject.activeSelf}, activeInHierarchy={cards[i].gameObject.activeInHierarchy}, size={(rect != null ? rect.rect.size.ToString() : "missing")}", cards[i]);
        }
    }
}
