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

    public void Configure(Image image, TMP_Text label, Image[] methods, TMP_Text[] labels)
    {
        icon = image; ingredientName = label; methodImages = methods; methodLabels = labels;
    }

    public void Bind(RecipeIngredientDisplayData data, RecipeVisualCatalog catalog)
    {
        if (icon != null) { icon.sprite = data?.sprite; icon.enabled = icon.sprite != null; }
        if (ingredientName != null) ingredientName.text = data?.name ?? "";
        BindMethods(data?.conditionFlag ?? IngredientState.None, catalog, methodImages, methodLabels);
    }

    public static void BindMethods(IngredientState flags, RecipeVisualCatalog catalog, Image[] images, TMP_Text[] labels)
    {
        IngredientState[] methods = { IngredientState.Cut, IngredientState.Grilled, IngredientState.Boiled, IngredientState.Roasted };
        string[] names = { "썰기", "굽기", "끓이기", "로스팅" };
        int slot = 0;
        for (int i = 0; i < methods.Length; i++)
        {
            if ((flags & methods[i]) == 0 || slot >= images.Length) continue;
            Image image = images[slot];
            Sprite sprite = catalog != null ? catalog.GetMethodIcon(methods[i]) : null;
            if (image != null) { image.gameObject.SetActive(true); image.sprite = sprite; image.enabled = sprite != null; }
            if (slot < labels.Length && labels[slot] != null)
            {
                labels[slot].gameObject.SetActive(true);
                labels[slot].text = sprite == null ? names[i] : "";
            }
            slot++;
        }
        for (int i = slot; i < images.Length; i++)
        {
            if (images[i] != null) images[i].gameObject.SetActive(false);
            if (i < labels.Length && labels[i] != null) labels[i].gameObject.SetActive(false);
        }
    }
}

