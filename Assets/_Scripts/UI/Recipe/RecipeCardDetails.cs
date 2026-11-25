using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RecipeCardDetails : MonoBehaviour
{
    [SerializeField] private LayoutElement dishLayout;
    [SerializeField] private RectTransform viewport;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text rewardLabel;
    [SerializeField] private RectTransform[] dishContent;
    [SerializeField, Min(0f)] private float expandedHeight = 64f;

    private Vector2[] collapsedOffsets;
    private float collapsedMinHeight;
    private float collapsedPreferredHeight;
    private float collapsedPreferredWidth;
    private float requiredTextWidth;

    public void Bind(string dishName, int rewardBeans)
    {
        nameLabel.text = dishName;
        rewardLabel.text = rewardBeans >= 0 ? $"황금콩 +{rewardBeans}" : "황금콩 —";
        requiredTextWidth = Mathf.Max(GetRequiredWidth(nameLabel), GetRequiredWidth(rewardLabel));
    }

    public void SetProgress(float heightProgress, float widthProgress, float ingredientWidth)
    {
        if (collapsedOffsets == null)
        {
            collapsedMinHeight = dishLayout.minHeight;
            collapsedPreferredHeight = dishLayout.preferredHeight;
            collapsedPreferredWidth = dishLayout.preferredWidth;
            collapsedOffsets = new Vector2[dishContent.Length];
            for (int i = 0; i < dishContent.Length; i++)
                collapsedOffsets[i] = new Vector2(dishContent[i].offsetMin.y, dishContent[i].offsetMax.y);
        }

        float naturalWidth = Mathf.Max(ingredientWidth, Mathf.Max(dishLayout.minWidth, collapsedPreferredWidth));
        dishLayout.preferredWidth = widthProgress > 0f && requiredTextWidth > naturalWidth
            ? Mathf.Lerp(naturalWidth, requiredTextWidth, widthProgress)
            : collapsedPreferredWidth;

        float height = expandedHeight * Mathf.Clamp01(heightProgress);
        dishLayout.minHeight = collapsedMinHeight + height;
        dishLayout.preferredHeight = collapsedPreferredHeight + height;

        // Keep the authored icon and timer aligned to the dish area's bottom as its top opens.
        for (int i = 0; i < dishContent.Length; i++)
        {
            RectTransform content = dishContent[i];
            var min = new Vector2(content.offsetMin.x, collapsedOffsets[i].x - height * content.anchorMin.y);
            var max = new Vector2(content.offsetMax.x, collapsedOffsets[i].y - height * content.anchorMax.y);
            content.offsetMin = min;
            content.offsetMax = max;
        }

        viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        viewport.gameObject.SetActive(height > 0f);
    }

    private float GetRequiredWidth(TMP_Text label)
    {
        // Both labels stretch across the mask. Keep space for their authored horizontal offsets.
        RectTransform rect = label.rectTransform;
        float labelInset = Mathf.Abs(rect.offsetMin.x) + Mathf.Abs(rect.offsetMax.x);
        float maskInset = Mathf.Max(0f, viewport.offsetMin.x - viewport.offsetMax.x);
        return Mathf.Ceil(label.GetPreferredValues(label.text).x + labelInset + maskInset + 1f);
    }
}
