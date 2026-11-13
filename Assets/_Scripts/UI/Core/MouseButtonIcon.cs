using UnityEngine;
using UnityEngine.UI;

// A scalable mouse glyph with the corresponding button filled. No panel or texture is needed.
[RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/Mouse Button Icon")]
public sealed class MouseButtonIcon : MaskableGraphic
{
    [SerializeField] private bool rightButton;
    public bool RightButton
    {
        get => rightButton;
        set { if (rightButton == value) return; rightButton = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float width = Mathf.Min(rect.width, rect.height * .7f);
        float height = Mathf.Min(rect.height, width / .7f);
        Vector2 center = rect.center;
        float radius = width * .5f;
        float straight = height * .5f - radius;
        const int segments = 40;
        Vector2 previous = Vector2.zero;
        Vector2 first = Vector2.zero;
        for (int i = 0; i <= segments; i++)
        {
            float angle = (90f + 360f * i / segments) * Mathf.Deg2Rad;
            Vector2 point = center + new Vector2(Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius + (Mathf.Sin(angle) >= 0f ? straight : -straight));
            if (i == 0) first = point;
            else Line(vh, previous, point, 1.5f, color);
            previous = point;
        }
        Line(vh, previous, first, 1.5f, color);
        float top = center.y + height * .5f;
        float split = center.y + height * .08f;
        Line(vh, new Vector2(center.x, top - 1f), new Vector2(center.x, split), 1.3f, color);
        Line(vh, new Vector2(center.x - radius + 1f, split),
            new Vector2(center.x + radius - 1f, split), 1.3f, color);
        // Button fill follows the rounded top of the mouse.
        float sign = rightButton ? 1f : -1f;
        Vector2 bottomInner = new(center.x + sign * 2.1f, split + 2.3f);
        Vector2 previousEdge = new(center.x + sign * 2.1f, top - 3f);
        const int buttonSegments = 8;
        for (int i = 1; i <= buttonSegments; i++)
        {
            float x = Mathf.Lerp(2.1f, radius - 2.7f, i / (float)buttonSegments);
            float normalized = x / Mathf.Max(.01f, radius - 2f);
            Vector2 edge = new(center.x + sign * x,
                center.y + straight + Mathf.Sqrt(Mathf.Max(0f, 1f - normalized * normalized)) * (radius - 2.5f));
            Triangle(vh, bottomInner, previousEdge, edge, color);
            previousEdge = edge;
        }
        Triangle(vh, bottomInner, previousEdge, new Vector2(previousEdge.x, split + 2.3f), color);
    }

    private static void Line(VertexHelper vh, Vector2 from, Vector2 to, float width, Color tint)
    {
        Vector2 delta = to - from;
        if (delta.sqrMagnitude < .00001f) return;
        Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
        int start = vh.currentVertCount;
        vh.AddVert(from - normal, tint, Vector2.zero);
        vh.AddVert(from + normal, tint, Vector2.zero);
        vh.AddVert(to + normal, tint, Vector2.zero);
        vh.AddVert(to - normal, tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, tint, Vector2.zero);
        vh.AddVert(b, tint, Vector2.zero);
        vh.AddVert(c, tint, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
    }
}
