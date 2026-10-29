using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class WeatherSymbolGraphic : MaskableGraphic
{
    public SkiWeather weather;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = GetPixelAdjustedRect();
        Vector2 center = r.center;
        float s = Mathf.Min(r.width, r.height) / 24;
        if (weather == SkiWeather.Sunny)
        {
            Disc(mesh, center, 4.2f * s);
            for (int i = 0; i < 8; i++)
            {
                Vector2 direction = new Vector2(Mathf.Cos(i * Mathf.PI / 4), Mathf.Sin(i * Mathf.PI / 4));
                Line(mesh, center + direction * 7 * s, center + direction * 10 * s, 1.5f * s);
            }
        }
        else if (weather == SkiWeather.Snow)
        {
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 side = new Vector2(-direction.y, direction.x);
                Line(mesh, center, center + direction * 10 * s, 1.5f * s);
                Line(mesh, center + direction * 5 * s, center + (direction * 7.5f + side * 3) * s, 1.3f * s);
                Line(mesh, center + direction * 5 * s, center + (direction * 7.5f - side * 3) * s, 1.3f * s);
            }
        }
        else
        {
            Disc(mesh, center + new Vector2(-5, 2) * s, 4 * s);
            Disc(mesh, center + new Vector2(0, 5) * s, 5 * s);
            Disc(mesh, center + new Vector2(5, 2) * s, 4 * s);
            Line(mesh, center + new Vector2(-6, 0) * s, center + new Vector2(6, 0) * s, 4 * s);
            for (int i = -1; i <= 1; i++)
                Line(mesh, center + new Vector2(i * 5 + 1, -5) * s, center + new Vector2(i * 5 - 1, -9) * s, 1.5f * s);
        }
    }

    private void Disc(VertexHelper mesh, Vector2 center, float radius)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i < 20; i++)
        {
            float angle = i * Mathf.PI * 2 / 20;
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
        }
        for (int i = 0; i < 20; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 20);
    }

    private void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width)
    {
        Vector2 side = new Vector2(-(to - from).y, (to - from).x).normalized * width * .5f;
        int start = mesh.currentVertCount;
        mesh.AddVert(from - side, color, Vector2.zero); mesh.AddVert(from + side, color, Vector2.zero);
        mesh.AddVert(to + side, color, Vector2.zero); mesh.AddVert(to - side, color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
    }
}
