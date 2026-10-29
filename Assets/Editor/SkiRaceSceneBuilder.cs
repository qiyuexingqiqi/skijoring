using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Authors persistent race UI and a real trigger at the existing finish arch.</summary>
public static class SkiRaceSceneBuilder
{
    private static Font font;
    private static readonly Color White = AlpineUITheme.Ink;
    private static readonly Color Muted = AlpineUITheme.Muted;
    private static readonly Color Blue = new Color(.48f, .84f, 1f);
    private static readonly Color Mint = new Color(.59f, .92f, .79f);

    [MenuItem("Tools/Skiing/Rebuild Race and Finish UI")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/AlpineUI.ttf");
        if (font == null) throw new System.Exception("Missing Alpine UI font");
        var gear = Object.FindObjectOfType<GearSelect>(true);
        var old = GameObject.Find("SkiRaceSystem");
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("SkiRaceSystem");
        var race = root.AddComponent<SkiRaceManager>();
        gear.raceManager = race;
        race.gearSelection = gear;
        var canvasObject = new GameObject("RaceCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.layer = 5;
        canvasObject.transform.SetParent(root.transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 800);
        scaler.matchWidthOrHeight = .5f;

        var hud = Surface("RaceTimer", canvas.transform, new Vector2(238, 90), 16);
        hud.rectTransform.anchorMin = hud.rectTransform.anchorMax = new Vector2(1, 1);
        hud.rectTransform.pivot = new Vector2(1, 1);
        hud.rectTransform.anchoredPosition = new Vector2(-24, -24);
        race.timerPanel = hud.gameObject;
        race.equipmentText = Label("Equipment", hud.transform, "单板滑雪 · 滑行计时", new Vector2(0, 22), new Vector2(214, 24), 13, Muted);
        race.timerText = Label("Clock", hud.transform, "00:00.00", new Vector2(0, -12), new Vector2(214, 44), 30, White, FontStyle.Bold);
        StyleTimer(race);

        var overlay = Surface("CompletionOverlay", canvas.transform, Vector2.zero, 0);
        Stretch(overlay.rectTransform);
        overlay.topColor = AlpineUITheme.OverlayTop;
        overlay.bottomColor = AlpineUITheme.OverlayBottom;
        overlay.borderWidth = 0;
        overlay.raycastTarget = true;
        race.completionPanel = overlay.gameObject;
        var card = Surface("FinishCard", overlay.transform, new Vector2(600, 438), 22);
        card.borderColor = AlpineUITheme.Border;
        var mark = Surface("CompletionMark", card.transform, new Vector2(48, 48), 24);
        mark.rectTransform.anchoredPosition = new Vector2(0, 160);
        mark.topColor = mark.bottomColor = AlpineUITheme.AccentSurface(Mint);
        mark.borderColor = new Color(Mint.r, Mint.g, Mint.b, .45f);
        Label("Check", mark.transform, "✓", Vector2.zero, new Vector2(46, 46), 29, new Color(.06f, .4f, .34f), FontStyle.Bold);
        Label("Title", card.transform, "恭喜您完成滑行！", new Vector2(0, 100), new Vector2(550, 50), 30, White, FontStyle.Bold);
        Label("Message", card.transform, "您成功冲过了终点线，漂亮的一滑！", new Vector2(0, 52), new Vector2(548, 34), 17, Muted);
        var result = Surface("Result", card.transform, new Vector2(504, 112), 14);
        result.rectTransform.anchoredPosition = new Vector2(0, -40);
        result.topColor = result.bottomColor = AlpineUITheme.Inset;
        result.borderWidth = 0;
        race.resultEquipmentText = Label("ResultLabel", result.transform, "单板滑雪  /  本次用时", new Vector2(0, 30), new Vector2(480, 24), 13, Muted);
        race.resultTimeText = Label("ResultTime", result.transform, "00:00.00", new Vector2(0, -12), new Vector2(480, 68), 43, AlpineUITheme.Emphasis, FontStyle.Bold);
        var retry = Action("Retry", card.transform, "再滑一次", new Vector2(-130, -154), Mint, race.Retry);
        var back = Action("Return", card.transform, "返回选择", new Vector2(130, -154), Blue, race.ReturnToSelection);
        race.retryButton = retry;
        retry.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = back, selectOnLeft = back };
        back.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = retry, selectOnLeft = retry };
        hud.gameObject.SetActive(false);
        overlay.gameObject.SetActive(false);

        // The existing large arch is at the end of Seg_05_终点, about 1.96 km from spawn.
        // World dimensions span the full opening and the banked snow surface beneath it.
        var gate = GameObject.Find("BoardShapeModel (1)");
        if (gate == null) throw new System.Exception("Cannot locate the existing finish arch");
        var previous = gate.transform.Find("FinishTrigger_终点");
        if (previous != null) Object.DestroyImmediate(previous.gameObject);
        var triggerObject = new GameObject("FinishTrigger_终点");
        triggerObject.layer = 2; // Ignore Raycast: it must not become camera/ground geometry.
        triggerObject.transform.position = new Vector3(35.2f, -1123f, 1911f);
        triggerObject.transform.rotation = Quaternion.Euler(0, gate.transform.eulerAngles.y, 0);
        triggerObject.transform.SetParent(gate.transform, true);
        var box = triggerObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        // Parent has an authored scale; compensate so the world trigger stays 66 x 46 x 6 m.
        var scale = triggerObject.transform.lossyScale;
        box.size = new Vector3(66 / Mathf.Abs(scale.x), 46 / Mathf.Abs(scale.y), 6 / Mathf.Abs(scale.z));
        triggerObject.AddComponent<SkiFinishTrigger>().race = race;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/race-scene-build.txt", "Saved integrated scene with race clock, finish UI and arch trigger at " + triggerObject.transform.position + ", bounds=" + box.bounds);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        return rect;
    }

    // Shared by both rebuild paths, so rebuilding the race system keeps the compact HUD layout.
    public static void StyleTimer(SkiRaceManager race)
    {
        var timer = race.timerPanel.GetComponent<RectTransform>();
        timer.anchorMin = timer.anchorMax = timer.pivot = Vector2.zero;
        timer.anchoredPosition = new Vector2(AlpineUITheme.HudMargin, AlpineUITheme.HudMargin);
        timer.localScale = Vector3.one * AlpineUITheme.HudScale;
        timer.sizeDelta = new Vector2(276, 48);
        var surface = timer.GetComponent<AlpinePanelGraphic>();
        AlpineUITheme.ApplyPanel(surface);
        surface.radius = 12;
        surface.SetVerticesDirty();
        var title = race.equipmentText;
        title.rectTransform.anchoredPosition = new Vector2(-65, 0);
        title.rectTransform.sizeDelta = new Vector2(126, 36);
        title.fontSize = 11; title.color = AlpineUITheme.Muted;
        race.timerText.rectTransform.anchoredPosition = new Vector2(66, 0);
        race.timerText.rectTransform.sizeDelta = new Vector2(120, 38);
        race.timerText.fontSize = 23; race.timerText.color = AlpineUITheme.Ink;
    }

    private static AlpinePanelGraphic Surface(string name, Transform parent, Vector2 size, float radius)
    {
        var surface = Rect(name, parent, size).gameObject.AddComponent<AlpinePanelGraphic>();
        AlpineUITheme.ApplyPanel(surface);
        surface.borderWidth = 1;
        surface.radius = radius;
        surface.raycastTarget = false;
        return surface;
    }

    private static Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color color, FontStyle style = FontStyle.Normal)
    {
        var rect = Rect(name, parent, size);
        rect.anchoredPosition = position;
        var label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        return label;
    }

    private static Button Action(string name, Transform parent, string text, Vector2 position, Color accent, UnityAction action)
    {
        var surface = Surface(name, parent, new Vector2(244, 48), 10);
        surface.rectTransform.anchoredPosition = position;
        surface.topColor = surface.bottomColor = AlpineUITheme.AccentSurface(accent);
        surface.raycastTarget = true;
        var label = Label("Label", surface.transform, text, Vector2.zero, new Vector2(244, 48), 16, new Color(.035f, .12f, .17f), FontStyle.Bold);
        var button = surface.gameObject.AddComponent<Button>();
        button.targetGraphic = surface;
        var colors = button.colors;
        colors.highlightedColor = new Color(.85f, 1, 1);
        colors.pressedColor = new Color(.65f, .85f, .9f);
        button.colors = colors;
        UnityEventTools.AddPersistentListener(button.onClick, action);
        return button;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public static void BuildAndValidate()
    {
        Build();
        SkiRaceValidation.Run();
    }
}
