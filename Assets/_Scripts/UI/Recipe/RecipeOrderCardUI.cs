using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Protocol;

public class RecipeOrderCardUI : MonoBehaviour
{
    [SerializeField] private TMP_Text dishName;
    [SerializeField] private Image dishImage;
    [SerializeField] private RecipeTimerRing timerFill;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private Image[] finalMethodImages = Array.Empty<Image>();
    [SerializeField] private TMP_Text[] finalMethodLabels = Array.Empty<TMP_Text>();
    [SerializeField] private RecipeIngredientUI ingredientPrefab;
    [SerializeField] private RectTransform ingredientRoot;
    private readonly System.Collections.Generic.List<RecipeIngredientUI> ingredientViews = new();
    public long OrderId { get; private set; } = -1;
    public bool NamesOnly { get; private set; }
    public RecipeCardData BoundData { get; private set; }
    private readonly System.Collections.Generic.Dictionary<Graphic, bool> hiddenGraphics = new();

    public void Configure(TMP_Text title, Image dish, RecipeTimerRing timer, TMP_Text seconds, Image[] methods,
        TMP_Text[] labels, RecipeIngredientUI ingredient, RectTransform content)
    {
        dishName = title; dishImage = dish; timerFill = timer; timeText = seconds;
        finalMethodImages = methods; finalMethodLabels = labels; ingredientPrefab = ingredient; ingredientRoot = content;
    }

    public void Bind(DishOrder order, RecipeCardData data, RecipeVisualCatalog catalog, bool namesOnly = true)
    {
        foreach (var entry in hiddenGraphics)
            if (entry.Key != null) entry.Key.enabled = entry.Value;
        hiddenGraphics.Clear();
        OrderId = order.Id;
        BoundData = data;
        NamesOnly = namesOnly;
        if (dishName != null)
            dishName.text = string.IsNullOrWhiteSpace(data?.name) ? $"Recipe #{order.RecipeId}" : data.name;
        if (namesOnly)
        {
            // Keep the card background and title; hide icons, recipe details and the timer.
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == dishName || graphic.gameObject == gameObject) continue;
                hiddenGraphics[graphic] = graphic.enabled;
                graphic.enabled = false;
            }
            gameObject.SetActive(true);
            return;
        }
        if (dishImage != null) { dishImage.sprite = data?.sprite; dishImage.enabled = dishImage.sprite != null; }
        RecipeIngredientUI.BindMethods(data?.finalConditionFlag ?? IngredientState.None, catalog, finalMethodImages, finalMethodLabels);
        int count = data?.ingredients?.Count ?? 0;
        while (ingredientViews.Count < count && ingredientPrefab != null && ingredientRoot != null)
            ingredientViews.Add(Instantiate(ingredientPrefab, ingredientRoot));
        for (int i = 0; i < ingredientViews.Count; i++)
        {
            ingredientViews[i].gameObject.SetActive(i < count);
            if (i < count) ingredientViews[i].Bind(data.ingredients[i], catalog);
        }
        gameObject.SetActive(true);
    }

    public void SetRemaining(DishOrder order, double now)
    {
        double remaining = order.Remaining(now);
        float fraction = order.DurationSeconds > 0 ? Mathf.Clamp01((float)(remaining / order.DurationSeconds)) : 1f;
        if (timerFill != null)
        {
            timerFill.FillAmount = fraction;
            timerFill.color = fraction > .66f ? Color.green : fraction > .33f ? new Color(1f, .5f, 0f) : Color.red;
        }
        if (timeText != null) timeText.text = double.IsPositiveInfinity(remaining) ? "--" : Math.Ceiling(remaining).ToString();
    }

    public void Clear() { OrderId = -1; gameObject.SetActive(false); }
}

