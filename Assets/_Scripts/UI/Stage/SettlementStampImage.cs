using UnityEngine;
using UnityEngine.UI;

// A temporary circle that remains a normal Image when a real stamp sprite is assigned.
[AddComponentMenu("UI/Settlement Stamp Image")]
public sealed class SettlementStampImage : Image
{
    [SerializeField, Range(16, 128)] private int segments = 64;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        if (sprite != null) { base.OnPopulateMesh(mesh); return; }
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (radius <= 0f) return;
        Vector2 center = rect.center;
        int count = Mathf.Clamp(segments, 16, 128);
        mesh.AddVert(center, color, new Vector2(0.5f, 0.5f));
        for (int i = 0; i <= count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            Vector2 offset = new(Mathf.Cos(angle), Mathf.Sin(angle));
            mesh.AddVert(center + offset * radius, color, (offset + Vector2.one) * 0.5f);
            if (i > 0) mesh.AddTriangle(0, i + 1, i);
        }
    }

    public override bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (sprite != null) return base.IsRaycastLocationValid(screenPoint, eventCamera);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out Vector2 local)) return false;
        Rect rect = GetPixelAdjustedRect();
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
        return radius > 0f && (local - rect.center).sqrMagnitude <= radius * radius;
    }
}
