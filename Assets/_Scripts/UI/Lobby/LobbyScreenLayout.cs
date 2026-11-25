using UnityEngine;

[DisallowMultipleComponent]
public sealed class LobbyScreenLayout : MonoBehaviour
{
    [SerializeField] private RectTransform content;
    [SerializeField] private Vector2 referenceSize = new(1600f, 900f);

    private void OnEnable() => RefreshLayout();
    private void OnRectTransformDimensionsChange() => RefreshLayout();

    public void RefreshLayout()
    {
        if (content == null || transform is not RectTransform root) return;
        Vector2 size = root.rect.size;
        if (size.x <= 0f || size.y <= 0f) return;
        float scale = Mathf.Min(size.x / referenceSize.x, size.y / referenceSize.y);
        content.localScale = Vector3.one * scale;
    }
}
