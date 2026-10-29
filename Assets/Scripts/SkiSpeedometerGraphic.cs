using UnityEngine;
using UnityEngine.UI;

/// <summary>Native UI geometry for the translucent ice-blue speed dial.</summary>
[AddComponentMenu("UI/Ski Speedometer")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SkiSpeedometerGraphic : MaskableGraphic
{
    [SerializeField, Min(1f)] private float maxSpeedKmh = 60f;
    private float speedKmh;
    public float MaxSpeedKmh => maxSpeedKmh;
    public float SpeedKmh => speedKmh;
    public float NormalizedSpeed => Mathf.Clamp01(speedKmh / Mathf.Max(1f, maxSpeedKmh));

    public void SetSpeed(float value)
    {
        value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Max(0, value);
        if (Mathf.Approximately(speedKmh, value)) return;
        speedKmh = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float scale = Mathf.Min(rect.width / 276f, rect.height / 162f);
        Vector2 origin = new Vector2(rect.center.x, rect.yMin + 23f * scale);
        float radius = 131f * scale;
        Color top = AlpineUITheme.PanelTop;
        Color bottom = AlpineUITheme.PanelBottom;
        // A half-round glass face with a straight lower edge.
        int start = mesh.currentVertCount;
        mesh.AddVert(origin, Color.Lerp(bottom, top, .15f) * color, Vector2.zero);
        mesh.AddVert(new Vector2(origin.x - radius, rect.yMin + 3 * scale), bottom * color, Vector2.zero);
        for (int i = 0; i <= 64; i++)
        {
            Vector2 point = Point(origin, radius, 180f - i * 180f / 64);
            mesh.AddVert(point, Color.Lerp(bottom, top, Mathf.InverseLerp(rect.yMin, rect.yMax, point.y)) * color, Vector2.zero);
        }
        mesh.AddVert(new Vector2(origin.x + radius, rect.yMin + 3 * scale), bottom * color, Vector2.zero);
        for (int i = 1; i < 67; i++) mesh.AddTriangle(start, start + i, start + i + 1);
        mesh.AddTriangle(start, start + 67, start + 1);

        Color cyan = new Color(.61f, .97f, 1f, .92f);
        Arc(mesh, origin, radius, 180, 0, 7 * scale, new Color(.30f, .88f, 1f, .13f));
        Arc(mesh, origin, radius, 180, 0, 1.8f * scale, cyan);
        Arc(mesh, origin, radius - 6 * scale, 180, 0, .8f * scale, new Color(.74f, .96f, 1, .45f));
        foreach (float x in new[] { -radius, radius })
            Line(mesh, origin + Vector2.right * x, new Vector2(origin.x + x, rect.yMin + 3 * scale), 1.8f * scale, cyan);
        Line(mesh, new Vector2(origin.x - radius, rect.yMin + 3 * scale), new Vector2(origin.x + radius, rect.yMin + 3 * scale), 1.8f * scale, cyan);

        const int segments = 30;
        float value = NormalizedSpeed * segments;
        for (int i = 0; i < segments; i++)
        {
            float from = 180 - i * 6f - .7f;
            float to = from - 4.6f;
            Arc(mesh, origin, 116 * scale, from, to, 10 * scale, new Color(.15f, .39f, .54f, .22f));
            float filled = Mathf.Clamp01(value - i);
            if (filled > 0)
                Arc(mesh, origin, 116 * scale, from, Mathf.Lerp(from, to, filled), 10 * scale,
                    Color.Lerp(new Color(.17f, .88f, 1, 1), new Color(.76f, 1, .95f, 1), i / 29f));
        }
        for (int i = 0; i <= 12; i++)
        {
            float angle = 180 - i * 15;
            Line(mesh, Point(origin, 105 * scale, angle), Point(origin, (i % 4 == 0 ? 97 : 101) * scale, angle),
                (i % 4 == 0 ? 1.6f : .8f) * scale, new Color(.15f, .37f, .50f, .65f));
        }
        // The moving cap gives continuous feedback between the discrete segments.
        float cap = 180 - NormalizedSpeed * 180;
        Arc(mesh, origin, 116 * scale, Mathf.Min(180, cap + 1.2f), Mathf.Max(0, cap - 1.2f), 12 * scale, new Color(.84f, 1f, 1f, 1));
    }

    private static Vector2 Point(Vector2 center, float radius, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
    }
    private void Arc(VertexHelper mesh, Vector2 center, float radius, float from, float to, float width, Color tint)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) / 3));
        int first = mesh.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(from, to, i / (float)steps);
            mesh.AddVert(Point(center, radius + width * .5f, angle), tint * color, Vector2.zero);
            mesh.AddVert(Point(center, radius - width * .5f, angle), tint * color, Vector2.zero);
            if (i == 0) continue;
            int a = first + (i - 1) * 2, b = a + 2;
            mesh.AddTriangle(a, b, a + 1); mesh.AddTriangle(a + 1, b, b + 1);
        }
    }
    private void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
        int first = mesh.currentVertCount;
        mesh.AddVert(a - side, tint * color, Vector2.zero); mesh.AddVert(a + side, tint * color, Vector2.zero);
        mesh.AddVert(b + side, tint * color, Vector2.zero); mesh.AddVert(b - side, tint * color, Vector2.zero);
        mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
    }
}
