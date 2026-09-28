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
    private int bindingVersion;
    private void OnDisable() => bindingVersion++;

    public void Configure(Image image, TMP_Text label, Image[] methods, TMP_Text[] labels)
    {
        icon = image; ingredientName = label; methodImages = methods; methodLabels = labels;
    }

    public void Bind(RecipeIngredientDisplayData data)
    {
        int version = ++bindingVersion;
        Func<bool> current = () => this != null && bindingVersion == version;
        RecipeSpriteLookup.Assign(icon, data?.iconName, current);
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
            RecipeSpriteLookup.Assign(image, RecipeSpriteLookup.MethodAddress(methods[i]), stillCurrent,
                slot < labels.Length ? labels[slot] : null);
            slot++;
        }
        for (int i = slot; i < images.Length; i++)
        {
            if (images[i] != null) images[i].gameObject.SetActive(false);
            if (i < labels.Length && labels[i] != null) labels[i].gameObject.SetActive(false);
        }
    }
}

