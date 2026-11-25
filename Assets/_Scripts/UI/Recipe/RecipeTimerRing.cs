using UnityEngine;
using UnityEngine.UI;

// Drawn directly by the Canvas so both displays can share the same countdown.
[RequireComponent(typeof(CanvasRenderer))]
public class RecipeTimerRing : MaskableGraphic
{
    [SerializeField, Range(0f, 1f)] private float fillAmount = 1f;
    [SerializeField, Range(.01f, .5f)] private float thickness = .15f;
    [SerializeField] private bool horizontalBar;
    [SerializeField] private Color barTrackColor = new Color32(110, 82, 61, 255);

    public float FillAmount
    {
        get => fillAmount;
        set { float next = Mathf.Clamp01(value); if (Mathf.Approximately(next, fillAmount)) return; fillAmount = next; SetVerticesDirty(); }
    }
    private static void AddBarQuad(VertexHelper vh, Rect rect, Color32 tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(new Vector3(rect.xMin, rect.yMin), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMin, rect.yMax), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMax), tint, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.yMin), tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        if (horizontalBar)
        {
            AddBarQuad(vh, rect, barTrackColor);
            if (fillAmount > 0f)
            {
                rect.width *= fillAmount;
                AddBarQuad(vh, rect, color);
            }
            return;
        }

        float outer = Mathf.Min(rect.width, rect.height) * .5f;
        float inner = outer * (1f - thickness);
        const int segments = 96;
        // Empty portion grows clockwise from twelve o'clock.
        float start = (1f - fillAmount) * Mathf.PI * 2f;
        int count = Mathf.CeilToInt(segments * fillAmount);
        for (int i = 0; i < count; i++)
        {
            float a = start + i * Mathf.PI * 2f / segments;
            float b = Mathf.Min(start + (i + 1) * Mathf.PI * 2f / segments, Mathf.PI * 2f);
            Vector2 da = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            Vector2 db = new Vector2(Mathf.Sin(b), Mathf.Cos(b));
            int v = vh.currentVertCount;
            vh.AddVert(rect.center + da * inner, color, Vector2.zero);
            vh.AddVert(rect.center + da * outer, color, Vector2.zero);
            vh.AddVert(rect.center + db * outer, color, Vector2.zero);
            vh.AddVert(rect.center + db * inner, color, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v, v + 2, v + 3);
        }
    }
}
