using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Protocol;

public class RecipeIngredientUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Image[] methodImages = Array.Empty<Image>();
    [SerializeField] private TMP_Text[] methodLabels = Array.Empty<TMP_Text>();
    [SerializeField] private RectTransform background;
    [Header("Font Sizes")]
    [SerializeField, Min(1f)] private float methodFontSize = 16f;
    private const float CompactBottomPadding = 4f;
    private bool compactBackground;
    private int bindingVersion;
    #region Unity Callbacks
    private void OnEnable()
    {
        ApplyTypography();
        ResizeCompactBackground();
    }
    private void OnRectTransformDimensionsChange() => ResizeCompactBackground();
    private void OnDisable() => bindingVersion++;
    #endregion

    public void Configure(Image image, Image[] methods, TMP_Text[] labels)
    {
        icon = image; methodImages = methods; methodLabels = labels;
        ApplyTypography();
    }

    private void ApplyTypography()
    {
        foreach (TMP_Text label in methodLabels) SetReadableFont(label, methodFontSize, 12f, 34f);
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
        int methodCount = BindMethods(data?.conditionFlag ?? IngredientState.None, methodImages, methodLabels, current);
        SetCompactBackground(methodCount == 0);
    }

    private void SetCompactBackground(bool compact)
    {
        if (background == null) return;

        if (compactBackground != compact)
        {
            compactBackground = compact;
            background.anchorMin = compact ? Vector2.up : Vector2.zero;
            background.sizeDelta = Vector2.zero;
        }
        ResizeCompactBackground();
    }

    private void ResizeCompactBackground()
    {
        if (!compactBackground || background == null || icon == null) return;

        // Resize the background within the existing slot to keep neighboring icons aligned.
        var tile = (RectTransform)transform;
        Bounds iconBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(tile, icon.rectTransform);
        float height = Mathf.Clamp(tile.rect.yMax - iconBounds.min.y + CompactBottomPadding, 0f, tile.rect.height);
        if (!Mathf.Approximately(background.sizeDelta.y, height))
            background.sizeDelta = new Vector2(0f, height);
    }

    public static int BindMethods(IngredientState flags, Image[] images, TMP_Text[] labels, Func<bool> stillCurrent)
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
        return slot;
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

