using UnityEngine;
using UnityEngine.UI;

/// <summary>Editable rounded UI surface with a vertical gradient and an inset border.</summary>
[AddComponentMenu("UI/Alpine Panel")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class AlpinePanelGraphic : MaskableGraphic
{
    public Color topColor = Color.white;
    public Color bottomColor = Color.white;
    public Color borderColor = Color.clear;
    public float radius = 18f;
    public float borderWidth = 1f;
    private const int Steps = 10;
    private const int Count = (Steps + 1) * 4;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect outer = GetPixelAdjustedRect();
        float inset = Mathf.Clamp(borderWidth, 0f, Mathf.Min(outer.width, outer.height) * 0.5f);
        Rect inner = new Rect(outer.x + inset, outer.y + inset, outer.width - inset * 2f, outer.height - inset * 2f);
        mesh.AddVert(inner.center, Tint(inner.center.y, outer), Vector2.zero);
        for (int i = 0; i < Count; i++)
        {
            Vector2 point = Edge(inner, Mathf.Max(0f, radius - inset), i);
            mesh.AddVert(point, Tint(point.y, outer), Vector2.zero);
        }
        for (int i = 0; i < Count; i++) mesh.AddTriangle(0, 1 + (i + 1) % Count, 1 + i);
        if (inset <= 0f) return;
        int start = mesh.currentVertCount;
        for (int i = 0; i < Count; i++)
        {
            mesh.AddVert(Edge(outer, radius, i), borderColor * color, Vector2.zero);
            mesh.AddVert(Edge(inner, Mathf.Max(0f, radius - inset), i), borderColor * color, Vector2.zero);
        }
        for (int i = 0; i < Count; i++)
        {
            int a = start + i * 2, b = start + ((i + 1) % Count) * 2;
            mesh.AddTriangle(a, a + 1, b);
            mesh.AddTriangle(a + 1, b + 1, b);
        }
    }

    private Color Tint(float y, Rect rect) => Color.Lerp(bottomColor, topColor, Mathf.InverseLerp(rect.yMin, rect.yMax, y)) * color;

    private static Vector2 Edge(Rect rect, float r, int index)
    {
        r = Mathf.Clamp(r, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        int corner = index / (Steps + 1);
        float angle = (-90f + corner * 90f + (index % (Steps + 1)) * 90f / Steps) * Mathf.Deg2Rad;
        Vector2 center = new Vector2(corner < 2 ? rect.xMax - r : rect.xMin + r,
            corner == 0 || corner == 3 ? rect.yMin + r : rect.yMax - r);
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
    }
}
