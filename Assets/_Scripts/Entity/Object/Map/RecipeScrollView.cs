using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Attach to the scroll root. Only the child cards are hidden; the 3D scroll remains visible.
public class RecipeScrollView : MonoBehaviour
{
    [SerializeField] private RectTransform cardRoot;
    [SerializeField] private RecipeOrderCardUI[] cards = Array.Empty<RecipeOrderCardUI>();
    public const int Capacity = 3;

    public void Configure(RectTransform root, RecipeOrderCardUI[] views) { cardRoot = root; cards = views; }
    private void Awake()
    {
        ConfigureLayout();
        // Another object's OnEnable may already have bound an order before our Awake.
        foreach (RecipeOrderCardUI card in cards)
            if (card != null && card.OrderId < 0) card.Clear();
    }
    private void ConfigureLayout()
    {
        if (cardRoot == null) return;

        VerticalLayoutGroup layout = cardRoot.GetComponent<VerticalLayoutGroup>();

        if (layout == null) layout = cardRoot.gameObject.AddComponent<VerticalLayoutGroup>();

        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }
    public void Show(IReadOnlyList<DishOrder> orders, RecipeVisualCatalog catalog, double now, bool namesOnly = true,
        Func<int, RecipeCardData> resolveRecipe = null)
    {
        for (int i = 0; i < cards.Length; i++)
        {
            RecipeOrderCardUI card = cards[i];

            if (card == null) continue;

            if (i >= Capacity || i >= orders.Count) { if (card.gameObject.activeSelf) card.Clear(); continue; }

            DishOrder order = orders[i];
            RecipeCardData data = resolveRecipe?.Invoke(order.RecipeId);

            if (card.OrderId != order.Id || card.NamesOnly != namesOnly || !ReferenceEquals(card.BoundData, data))
                card.Bind(order, data, catalog, namesOnly);

            if (!namesOnly) card.SetRemaining(order, now);
        }
    }
    public void Clear()
    {
        foreach (RecipeOrderCardUI card in cards) if (card != null) card.Clear();
    }

    public void ReportUIState()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Debug.Log($"RecipeScrollView {name}: active={gameObject.activeInHierarchy}, canvas={(canvas != null ? canvas.renderMode.ToString() : "missing")}, root={(cardRoot != null ? cardRoot.rect.size.ToString() : "missing")}, cards={cards.Length}", this);
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) { Debug.LogWarning($"Card {i + 1}: missing reference", this); continue; }
            RectTransform rect = cards[i].GetComponent<RectTransform>();
            Debug.Log($"Card {i + 1}: order={cards[i].OrderId}, activeSelf={cards[i].gameObject.activeSelf}, activeInHierarchy={cards[i].gameObject.activeInHierarchy}, size={(rect != null ? rect.rect.size.ToString() : "missing")}", cards[i]);
        }
    }
}
