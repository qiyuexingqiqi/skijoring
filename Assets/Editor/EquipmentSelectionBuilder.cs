using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.UI;

public static class EquipmentSelectionBuilder
{
    private static Font font;
    private static readonly Color White = AlpineUITheme.Ink;
    private static readonly Color Muted = AlpineUITheme.Muted;
    private static readonly Color Blue = new Color(0.48f, 0.84f, 1f);
    private static readonly Color Mint = new Color(0.59f, 0.92f, 0.79f);

    [MenuItem("Tools/Skiing/Rebuild Equipment Selection UI")]
    public static void Build()
    {
        Directory.CreateDirectory("Temp/EquipmentUI");
        if (!File.Exists("Temp/EquipmentUI/Snowfield_Integrated.before-selection.unity"))
            File.Copy(SnowboardIntegrationBuild.ScenePath, "Temp/EquipmentUI/Snowfield_Integrated.before-selection.unity");
        AssetDatabase.Refresh();
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/AlpineUI.ttf");
        if (font == null) throw new System.Exception("Missing bundled AlpineUI font");
        var scene = EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        var selection = Object.FindObjectOfType<GearSelect>(true);
        var canvas = selection.selectUIPanel.GetComponent<Canvas>();
        if (canvas == null) throw new System.Exception("Selection must reference its existing Canvas");
        // Preserve the weather controls and all their existing scene references.
        var old = canvas.transform.Find("SlectPanel");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var previous = canvas.transform.Find("EquipmentSelection");
        if (previous != null) Object.DestroyImmediate(previous.gameObject);
        canvas.sortingOrder = 30;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 800f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var screen = Surface("EquipmentSelection", canvas.transform, Vector2.zero, Vector2.zero,
            AlpineUITheme.OverlayTop, AlpineUITheme.OverlayBottom, Color.clear, 0f);
        Stretch(screen.rectTransform, 0f, 0f, 0f, 0f);
        screen.raycastTarget = true;
        var layout = screen.gameObject.AddComponent<EquipmentSelectionLayout>();
        var content = Rect("Content", screen.transform, Vector2.zero, new Vector2(800f, 490f));
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
        layout.content = content;

        var title = Label("Title", content, "装备选择", Vector2.zero, new Vector2(0f, 58f), 32, White, FontStyle.Bold);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.alignment = TextAnchor.MiddleCenter;

        var single = Card(content, false, selection.OnClickSingle);
        var dual = Card(content, true, selection.OnClickDouble);
        layout.singleCard = single.GetComponent<RectTransform>();
        layout.doubleCard = dual.GetComponent<RectTransform>();
        var navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = dual, selectOnDown = dual };
        single.navigation = navigation;
        dual.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = single, selectOnUp = single };

        var footer = Rect("Footer", content, new Vector2(0f, -452f), new Vector2(0f, 28f));
        footer.anchorMax = new Vector2(1f, 1f);
        layout.footer = footer;
        var hint = Label("Start Hint", footer, "点击卡片，开启滑行", Vector2.zero, new Vector2(0f, 27f), 12, Muted);
        hint.rectTransform.anchorMax = new Vector2(1f, 1f);
        hint.alignment = TextAnchor.MiddleCenter;
        Canvas.ForceUpdateCanvases();
        layout.Refresh();

        var events = Object.FindObjectOfType<EventSystem>();
        if (events == null) events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).GetComponent<EventSystem>();
        events.firstSelectedGameObject = single.gameObject;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/equipment-selection-build.txt", "Equipment selection saved. Existing single/double board bindings and weather controls preserved.");
    }

    private static Button Card(Transform parent, bool dual, UnityAction action)
    {
        Color accent = dual ? Mint : Blue;
        var surface = Surface(dual ? "DoubleBoardCard" : "SingleBoardCard", parent, Vector2.zero, new Vector2(388f, 338f),
            AlpineUITheme.PanelTop, AlpineUITheme.PanelBottom,
            new Color(accent.r, accent.g, accent.b, 0.22f), 20f);
        surface.raycastTarget = true;
        var button = surface.gameObject.AddComponent<Button>();
        button.targetGraphic = surface;
        button.transition = Selectable.Transition.None;
        UnityEventTools.AddPersistentListener(button.onClick, action);
        var feedback = surface.gameObject.AddComponent<EquipmentCardFeedback>();
        feedback.surface = surface;
        feedback.accent = accent;
        var name = Label("Equipment Name", surface.transform, dual ? "双板滑雪" : "单板滑雪", new Vector2(0f, -218f), new Vector2(0f, 44f), 26, White, FontStyle.Bold);
        name.rectTransform.anchorMax = new Vector2(1f, 1f);
        name.alignment = TextAnchor.MiddleCenter;

        var artRect = Rect("Equipment Illustration", surface.transform, new Vector2(0f, -20f), new Vector2(170f, 190f));
        artRect.anchorMin = artRect.anchorMax = artRect.pivot = new Vector2(0.5f, 1f);
        var art = artRect.gameObject.AddComponent<EquipmentBoardGraphic>();
        art.doubleBoard = dual;
        art.accent = accent;
        art.raycastTarget = false;

        Color actionFill = AlpineUITheme.AccentSurface(accent);
        var cta = Surface("Action", surface.transform, new Vector2(24f, -278f), new Vector2(-48f, 40f), actionFill, actionFill, Color.clear, 9f);
        cta.rectTransform.anchorMax = new Vector2(1f, 1f);
        var actionLabel = Label("Action Label", cta.transform, dual ? "选择双板" : "选择单板", Vector2.zero, Vector2.zero, 15, new Color(0.035f, 0.12f, 0.17f));
        Stretch(actionLabel.rectTransform, 0f, 0f, 0f, 0f);
        actionLabel.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static AlpinePanelGraphic Surface(string name, Transform parent, Vector2 position, Vector2 size,
        Color top, Color bottom, Color border, float radius)
    {
        var rect = Rect(name, parent, position, size);
        var surface = rect.gameObject.AddComponent<AlpinePanelGraphic>();
        surface.topColor = top;
        surface.bottomColor = bottom;
        surface.borderColor = border;
        surface.radius = radius;
        surface.borderWidth = border.a > 0f ? 1f : 0f;
        surface.raycastTarget = false;
        return surface;
    }

    private static Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size,
        int fontSize, Color color, FontStyle style = FontStyle.Normal)
    {
        var rect = Rect(name, parent, position, size);
        var label = rect.gameObject.AddComponent<Text>();
        label.text = text;
        label.font = font;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = color;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.alignment = TextAnchor.UpperLeft;
        label.lineSpacing = 1.2f;
        return label;
    }

    public static void BuildAndValidate()
    {
        Build();
        EquipmentSelectionValidation.Run();
    }

    private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }
    public static void Inspect()
    {
        EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        var output = new StringBuilder();
        var selection = Object.FindObjectOfType<GearSelect>(true);
        output.AppendLine("Selector " + selection.name + " single=" + selection.boardSingle.name + " double=" + selection.boardDouble.name);
        foreach (var transform in selection.selectUIPanel.GetComponentsInChildren<Transform>(true))
            output.AppendLine("UI " + transform.name + " parent=" + transform.parent?.name + " components="
                + string.Join(",", System.Array.ConvertAll(transform.GetComponents<Component>(), c => c ? c.GetType().Name : "missing")));
        foreach (var camera in Object.FindObjectsOfType<Camera>(true))
            output.AppendLine("Camera " + camera.name + " active=" + camera.gameObject.activeInHierarchy + " pos=" + camera.transform.position
                + " components=" + string.Join(",", System.Array.ConvertAll(camera.GetComponents<Component>(), c => c ? c.GetType().Name : "missing")));
        foreach (var player in Object.FindObjectsOfType<Snowboard.PlayerController>(true))
            output.AppendLine("SinglePlayer " + player.name + " active=" + player.gameObject.activeSelf);
        File.WriteAllText("Logs/equipment-selection-inspection.txt", output.ToString());
    }
}
