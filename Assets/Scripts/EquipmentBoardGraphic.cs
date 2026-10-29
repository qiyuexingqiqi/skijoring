using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent snowboard / ski illustrations for the selection cards.</summary>
[AddComponentMenu("UI/Equipment Illustration")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class EquipmentBoardGraphic : MaskableGraphic
{
    public bool doubleBoard;
    public Color accent = new Color(0.45f, 0.83f, 1f);
    private VertexHelper mesh;
    private Vector2 origin;
    private float scale;

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        mesh = vertices;
        Rect rect = GetPixelAdjustedRect();
        origin = rect.center;
        scale = Mathf.Min(rect.width / 210f, rect.height / 260f);
        Ring(new Vector2(0f, 0f), 90f, 1f, Alpha(accent, 0.12f));
        Ring(new Vector2(0f, 0f), 65f, 1f, Alpha(accent, 0.06f));
        if (doubleBoard)
        {
            Capsule(new Vector2(69f, 0f), new Vector2(3f, 225f), -17f, Alpha(accent, 0.65f));
            Part(new Vector2(69f, 0f), new Vector2(0f, 100f), new Vector2(10f, 28f), -17f, new Color(0.78f, 0.9f, 0.96f));
            Part(new Vector2(69f, 0f), new Vector2(0f, -104f), new Vector2(18f, 4f), -17f, accent);
            Ski(new Vector2(-27f, 0f), -14f);
            Ski(new Vector2(15f, 0f), -14f);
        }
        else
        {
            Board(new Vector2(6f, 0f), -23f);
        }
    }

    private void Board(Vector2 center, float rotation)
    {
        Capsule(center + new Vector2(8f, -7f), new Vector2(53f, 239f), rotation, new Color(0f, 0f, 0f, 0.23f));
        Capsule(center, new Vector2(53f, 239f), rotation, new Color(0.86f, 0.95f, 0.99f));
        Part(center, new Vector2(-16f, 0f), new Vector2(12f, 192f), rotation, accent);
        Part(center, new Vector2(9f, 75f), new Vector2(14f, 44f), rotation, accent);
        Part(center, new Vector2(14f, -84f), new Vector2(6f, 31f), rotation, accent);
        Part(center, new Vector2(1f, 0f), new Vector2(27f, 4f), rotation, new Color(0.13f, 0.30f, 0.40f));
        Binding(center, new Vector2(0f, 39f), rotation, 29f);
        Binding(center, new Vector2(0f, -39f), rotation, 29f);
    }

    private void Ski(Vector2 center, float rotation)
    {
        Capsule(center + new Vector2(5f, -6f), new Vector2(23f, 241f), rotation, new Color(0f, 0f, 0f, 0.23f));
        Capsule(center, new Vector2(23f, 241f), rotation, new Color(0.84f, 0.97f, 0.94f));
        Part(center, new Vector2(0f, 78f), new Vector2(15f, 55f), rotation, accent);
        Part(center, new Vector2(-4f, -28f), new Vector2(4f, 122f), rotation, accent);
        Part(center, new Vector2(4f, -91f), new Vector2(5f, 24f), rotation, accent);
        Binding(center, new Vector2(0f, -3f), rotation, 17f);
    }

    private void Binding(Vector2 center, Vector2 offset, float rotation, float width)
    {
        Part(center, offset, new Vector2(width, 34f), rotation, new Color(0.07f, 0.18f, 0.25f));
        Part(center, offset + new Vector2(0f, 7f), new Vector2(width - 5f, 7f), rotation, new Color(0.51f, 0.65f, 0.71f));
        Part(center, offset - new Vector2(0f, 9f), new Vector2(width - 5f, 4f), rotation, new Color(0.51f, 0.65f, 0.71f));
    }

    private void Part(Vector2 center, Vector2 offset, Vector2 size, float rotation, Color fill)
        => Capsule(center + Rotate(offset, rotation), size, rotation, fill);

    private void Capsule(Vector2 center, Vector2 size, float rotation, Color fill)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(origin + center * scale, fill * color, Vector2.zero);
        float radius = Mathf.Min(size.x, size.y) * 0.5f;
        const int steps = 9;
        const int count = 4 * (steps + 1);
        for (int i = 0; i < count; i++)
        {
            int corner = i / (steps + 1);
            float angle = (-90f + corner * 90f + (i % (steps + 1)) * 90f / steps) * Mathf.Deg2Rad;
            Vector2 point = new Vector2(corner < 2 ? size.x * 0.5f - radius : -size.x * 0.5f + radius,
                corner == 0 || corner == 3 ? -size.y * 0.5f + radius : size.y * 0.5f - radius);
            point += new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            mesh.AddVert(origin + (center + Rotate(point, rotation)) * scale, fill * color, Vector2.zero);
        }
        for (int i = 0; i < count; i++) mesh.AddTriangle(start, start + 1 + (i + 1) % count, start + 1 + i);
    }

    private void Ring(Vector2 center, float radius, float thickness, Color fill)
    {
        int start = mesh.currentVertCount;
        const int count = 90;
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = new Vector2(Mathf.Cos(i * Mathf.PI * 2f / count), Mathf.Sin(i * Mathf.PI * 2f / count));
            mesh.AddVert(origin + (center + direction * radius) * scale, fill * color, Vector2.zero);
            mesh.AddVert(origin + (center + direction * (radius - thickness)) * scale, fill * color, Vector2.zero);
        }
        for (int i = 0; i < count; i++)
        {
            int a = start + i * 2, b = start + ((i + 1) % count) * 2;
            mesh.AddTriangle(a, a + 1, b);
            mesh.AddTriangle(a + 1, b + 1, b);
        }
    }

    private static Vector2 Rotate(Vector2 point, float angle)
    {
        float rad = angle * Mathf.Deg2Rad;
        return new Vector2(point.x * Mathf.Cos(rad) - point.y * Mathf.Sin(rad), point.x * Mathf.Sin(rad) + point.y * Mathf.Cos(rad));
    }
    private static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
}
