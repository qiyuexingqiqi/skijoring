using UnityEngine;

/// <summary>Shared translucent ice-blue surfaces and legible dark text over the snowfield.</summary>
public static class AlpineUITheme
{
    public const float HudScale = .8f;
    public const float HudMargin = 20f;
    public static readonly Color Ink = new Color(.055f, .16f, .24f);
    public static readonly Color Muted = new Color(.16f, .30f, .40f);
    public static readonly Color Emphasis = new Color(.055f, .32f, .46f);
    public static readonly Color PanelTop = new Color(.79f, .92f, 1f, .42f);
    public static readonly Color PanelBottom = new Color(.59f, .81f, .94f, .54f);
    public static readonly Color Border = new Color(.88f, .97f, 1f, .60f);
    public static readonly Color Inset = new Color(.87f, .96f, 1f, .24f);
    public static readonly Color OverlayTop = new Color(.73f, .89f, 1f, .12f);
    public static readonly Color OverlayBottom = new Color(.65f, .84f, .96f, .20f);

    public static Color AccentSurface(Color accent) => new Color(accent.r, accent.g, accent.b, .48f);

    public static void ApplyPanel(AlpinePanelGraphic surface)
    {
        surface.topColor = PanelTop;
        surface.bottomColor = PanelBottom;
        surface.borderColor = Border;
        surface.SetVerticesDirty();
    }
}
