using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Protocol;

public class RecipeIngredientUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text ingredientName;
    [SerializeField] private Image[] methodImages = Array.Empty<Image>();
    [SerializeField] private TMP_Text[] methodLabels = Array.Empty<TMP_Text>();
    [Header("Font Sizes")]
    [SerializeField, Min(1f)] private float ingredientFontSize = 22f;
    [SerializeField, Min(1f)] private float methodFontSize = 16f;
    private int bindingVersion;
    #region Unity Callbacks
    private void OnEnable() => ApplyTypography();
    private void OnDisable() => bindingVersion++;
    #endregion

    public void Configure(Image image, TMP_Text label, Image[] methods, TMP_Text[] labels)
    {
        icon = image; ingredientName = label; methodImages = methods; methodLabels = labels;
        ApplyTypography();
    }

    private void ApplyTypography()
    {
        SetReadableFont(ingredientName, ingredientFontSize, 18f, 30f);
        foreach (TMP_Text label in methodLabels) SetReadableFont(label, methodFontSize, 12f, 34f);
        // Give the larger name its own line above the method icons.
        foreach (Image image in methodImages)
            if (image != null) MoveMethodBelowName(image.rectTransform);
        foreach (TMP_Text label in methodLabels)
            if (label != null) MoveMethodBelowName(label.rectTransform);
        if (transform is RectTransform rect)
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(rect.rect.height, 164f));
        LayoutElement layout = GetComponent<LayoutElement>();
        if (layout != null) layout.preferredHeight = Mathf.Max(layout.preferredHeight, 164f);
    }

    private void MoveMethodBelowName(RectTransform rect)
    {
        if (rect.parent != transform) return;
        Vector2 position = rect.anchoredPosition;
        position.y = Mathf.Min(position.y, -130f);
        rect.anchoredPosition = position;
    }

    public static void SetReadableFont(TMP_Text text, float preferred, float minimum, float height)
    {
        if (text == null) return;
        text.enableAutoSizing = true;
        text.fontSize = preferred;
        text.fontSizeMax = preferred;
        text.fontSizeMin = Mathf.Min(preferred, minimum);
        RectTransform rect = text.rectTransform;
        // Stretched fallback labels already use the icon's full rectangle.
        if (Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(rect.rect.height, height));
    }

    public void Bind(RecipeIngredientDisplayData data)
    {
        ApplyTypography();
        int version = ++bindingVersion;
        Func<bool> current = () => this != null && bindingVersion == version;
        SpriteCache.Assign(icon, data?.iconName, current);
        if (ingredientName != null) ingredientName.text = data == null ? "" : $"{data.name} ×{data.amount}";
        BindMethods(data?.conditionFlag ?? IngredientState.None, methodImages, methodLabels, current);
    }

    public static void BindMethods(IngredientState flags, Image[] images, TMP_Text[] labels, Func<bool> stillCurrent)
    {
        IngredientState[] methods = { IngredientState.Cut, IngredientState.Grilled, IngredientState.Boiled, IngredientState.Roasted };
        string[] names = { "썰기", "굽기", "끓이기", "로스팅" };
        int slot = 0;
        for (int i = 0; i < methods.Length; i++)
        {
            if ((flags & methods[i]) == 0 || slot >= images.Length) continue;
            Image image = images[slot];
            if (image != null) image.gameObject.SetActive(true);
            if (slot < labels.Length && labels[slot] != null)
            {
                labels[slot].gameObject.SetActive(true);
                labels[slot].text = names[i];
            }
            SpriteCache.Assign(image, GetMethodIconAddress(methods[i]), stillCurrent,
                slot < labels.Length ? labels[slot] : null);
            slot++;
        }
        for (int i = slot; i < images.Length; i++)
        {
            if (images[i] != null) images[i].gameObject.SetActive(false);
            if (i < labels.Length && labels[i] != null) labels[i].gameObject.SetActive(false);
        }
    }

    private static string GetMethodIconAddress(IngredientState method) => method switch
    {
        IngredientState.Cut => "Cutted_Icon",
        IngredientState.Grilled => "Pan_Icon",
        IngredientState.Boiled => "Pot_Icon",
        IngredientState.Roasted => "Oven_Icon",
        _ => null
    };

}

