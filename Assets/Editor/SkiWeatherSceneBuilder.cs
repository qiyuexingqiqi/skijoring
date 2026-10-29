using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UnityEngine.Events;
using UnityEngine;
using UnityEngine.UI;

public static partial class SkiWeatherSceneBuilder
{
    private static Font font;
    private static readonly Color White = AlpineUITheme.Ink;
    private static readonly Color Muted = AlpineUITheme.Muted;
    private static readonly Color Blue = new Color(.48f, .84f, 1f);

    [MenuItem("Tools/Skiing/Rebuild Weather and Gameplay UI")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/AlpineUI.ttf");
        var transforms = Object.FindObjectsOfType<Transform>(true);
        var rain = transforms.First(t => t.name == "Rain").gameObject;
        var snow = transforms.First(t => t.name == "Snowfall").gameObject;
        rain.transform.SetParent(null, true); snow.transform.SetParent(null, true);
        var old = GameObject.Find("SkiWeatherSystem");
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("SkiWeatherSystem");
        var weather = root.AddComponent<SkiWeatherController>();
        var anchor = new GameObject("WeatherEffectsAnchor").transform;
        anchor.SetParent(root.transform, false);
        anchor.position = Camera.main.transform.position;
        weather.effectsAnchor = anchor;
        weather.followCamera = Camera.main;
        weather.rainEffect = rain;
        weather.snowEffect = snow;
        weather.initialWeather = SkiWeather.Sunny;
        weather.sunLight = Object.FindObjectsOfType<Light>().FirstOrDefault(light => light.type == LightType.Directional);
        ConfigureEffect(rain, anchor, false);
        ConfigureEffect(snow, anchor, true);
        var race = Object.FindObjectOfType<SkiRaceManager>();
        var gear = Object.FindObjectOfType<GearSelect>();
        var selectionLayout = gear.selectUIPanel.GetComponentInChildren<EquipmentSelectionLayout>(true);
        foreach (var name in new[] { "WeatherPanel", "ControlsPanel" })
        {
            var oldSetting = selectionLayout.content.Find(name);
            if (oldSetting != null) Object.DestroyImmediate(oldSetting.gameObject);
        }
        foreach (var name in new[] { "下雨", "下雪" })
        {
            var button = gear.selectUIPanel.transform.Find(name);
            if (button != null) Object.DestroyImmediate(button.gameObject);
        }

        var canvasObject = new GameObject("GameplayCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.layer = 5;
        canvasObject.transform.SetParent(root.transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 800);
        scaler.matchWidthOrHeight = .5f;
        var hud = root.AddComponent<SkiGameplayHUD>();
        hud.weather = weather; hud.race = race;
        hud.viewManager = Object.FindObjectOfType<CameraViewManager>();
        hud.canvasRect = (RectTransform)canvas.transform;
        var panel = Panel("WeatherPanel", selectionLayout.content, new Vector2(388, 112), new Vector2(0, 1), Vector2.zero);
        selectionLayout.weatherPanel = panel.rectTransform;
        hud.weatherPanel = panel.gameObject;
        Label("WeatherTitle", panel.transform, "天气选择", new Vector2(0, 32), new Vector2(348, 24), 13, Muted, false, TextAnchor.MiddleLeft);
        hud.weatherButtons = new Button[3]; hud.weatherLabels = new Text[3]; hud.weatherSymbols = new WeatherSymbolGraphic[3];
        string[] weatherNames = { "晴天", "下雪", "下雨" };
        UnityAction[] actions = { weather.SelectSunny, weather.SelectSnow, weather.SelectRain };
        for (int i = 0; i < 3; i++)
        {
            var button = Action(weatherNames[i], panel.transform, new Vector2(108, 44), new Vector2((i - 1) * 116, -15), actions[i]);
            var text = Label("Label", button.transform, weatherNames[i], new Vector2(12, 0), new Vector2(48, 30), 14, White);
            var symbolRect = Rect("Symbol", button.transform, new Vector2(21, 21));
            symbolRect.anchoredPosition = new Vector2(-24, 0);
            var symbol = symbolRect.gameObject.AddComponent<WeatherSymbolGraphic>();
            symbol.weather = (SkiWeather)i; symbol.raycastTarget = false;
            hud.weatherButtons[i] = button; hud.weatherLabels[i] = text; hud.weatherSymbols[i] = symbol;
        }
        for (int i = 0; i < 3; i++)
            hud.weatherButtons[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnLeft = hud.weatherButtons[(i + 2) % 3], selectOnRight = hud.weatherButtons[(i + 1) % 3] };
        var speed = Rect("SpeedPanel", canvas.transform, new Vector2(276, 162));
        speed.anchorMin = speed.anchorMax = speed.pivot = Vector2.zero;
        speed.localScale = Vector3.one * AlpineUITheme.HudScale;
        speed.anchoredPosition = new Vector2(AlpineUITheme.HudMargin, AlpineUITheme.HudMargin + 58 * AlpineUITheme.HudScale);
        hud.speedPanel = speed.gameObject;
        hud.speedometer = speed.gameObject.AddComponent<SkiSpeedometerGraphic>();
        hud.speedometer.raycastTarget = false;
        var dialInk = AlpineUITheme.Muted;
        Label("SpeedTitle", speed, "当前速度", new Vector2(0, 11), new Vector2(100, 22), 11, dialInk);
        hud.speedText = Label("Speed", speed, "0.0", new Vector2(0, -24), new Vector2(172, 76), 46, AlpineUITheme.Ink, true);
        var shadow = hud.speedText.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(.9f, 1f, 1f, .35f); shadow.effectDistance = new Vector2(0, -1);
        Label("Unit", speed, "km/h", new Vector2(0, -57), new Vector2(86, 23), 13, dialInk);
        for (int i = 0; i < 4; i++)
        {
            float angle = (180 - i * 60) * Mathf.Deg2Rad;
            var position = new Vector2(Mathf.Cos(angle) * 88, -58 + Mathf.Sin(angle) * 88);
            Label("Scale" + i, speed, (i * 20).ToString(), position, new Vector2(28, 20), 10, dialInk);
        }
        SkiRaceSceneBuilder.StyleTimer(race);
        var view = Panel("ViewPanel", canvas.transform, new Vector2(208, 94), new Vector2(1, 0), new Vector2(-AlpineUITheme.HudMargin, AlpineUITheme.HudMargin));
        view.rectTransform.localScale = Vector3.one * AlpineUITheme.HudScale;
        hud.viewPanel = view.gameObject;
        Label("ViewTitle", view.transform, "切换视角", new Vector2(0, 25), new Vector2(176, 24), 13, Muted);
        var viewButton = Action("NextView", view.transform, new Vector2(176, 38), new Vector2(0, -14), hud.NextView);
        hud.viewText = Label("ViewLabel", viewButton.transform, "追随视角  ·  V", Vector2.zero, new Vector2(172, 34), 14, AlpineUITheme.Emphasis);
        viewButton.navigation = new Navigation { mode = Navigation.Mode.None };
        var controls = Panel("ControlsPanel", selectionLayout.content, new Vector2(388, 112), new Vector2(0, 1), Vector2.zero);
        selectionLayout.controlsPanel = controls.rectTransform;
        hud.controlsPanel = controls.gameObject;
        Label("ControlsTitle", controls.transform, "操作说明", new Vector2(0, 32), new Vector2(348, 24), 13, Muted, false, TextAnchor.MiddleLeft);
        hud.controlsText = Label("Controls", controls.transform,
            "W / ↑ 加速    S / ↓ 刹车\nA / D / ← / → 转向    Space 单板起跳\nV 切换视角    R 返回选择",
            new Vector2(0, -13), new Vector2(348, 62), 13, Muted, false, TextAnchor.MiddleLeft);
        speed.gameObject.SetActive(false); view.gameObject.SetActive(false);
        hud.Refresh();
        Canvas.ForceUpdateCanvases();
        selectionLayout.Refresh();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/weather-ui-build.txt", "Saved Sunny/Snow/Rain and controls on the equipment selection page. Skiing shows speed, race time and camera view only; effects follow the rendering camera.");
    }

    private static void ConfigureEffect(GameObject effect, Transform anchor, bool snow)
    {
        foreach (var p in effect.GetComponentsInChildren<ParticleSystem>(true)) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.SetActive(false);
        effect.transform.SetParent(anchor, false);
        effect.transform.localPosition = snow ? new Vector3(0, 10, 10) : new Vector3(0, 18, 10);
        effect.transform.localRotation = snow ? Quaternion.identity : Quaternion.Euler(90, 0, 0);
        effect.transform.localScale = Vector3.one;
        foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            bool primary = particles.gameObject == effect;
            if (!snow && !primary) continue; // Preserve the original rain collision subeffects.
            var main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true; main.prewarm = true;
            main.startLifetime = snow ? 12 : 1;
            main.startSpeed = snow ? 0 : 45;
            main.maxParticles = snow ? 3000 : 5000;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = snow ? new Vector3(48, .1f, 48) : new Vector3(42, 42, .1f);
            shape.randomDirectionAmount = 0;
            shape.sphericalDirectionAmount = 0;
            shape.rotation = Vector3.zero;
            var emission = particles.emission;
            emission.rateOverTime = snow ? (primary ? 130 : 40) : 3000;
            if (snow)
            {
                main.gravityModifier = 0;
                var velocity = particles.velocityOverLifetime;
                velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(-.15f);
                velocity.y = new ParticleSystem.MinMaxCurve(-3f);
                velocity.z = new ParticleSystem.MinMaxCurve(-.25f);
            }
            else
            {
                // The supplied rain randomized every drop's direction and stretched it to 4 m.
                // Use downward streaks around the active view so it reads as rain while skiing.
                main.startSize = .06f;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 12;
                renderer.velocityScale = .01f;
            }
        }
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = size;
        return rect;
    }

    private static AlpinePanelGraphic Panel(string name, Transform parent, Vector2 size, Vector2 anchor, Vector2 position)
    {
        var rect = Rect(name, parent, size); rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = position;
        // Children use center coordinates even for panels anchored to a screen corner.
        var surface = rect.gameObject.AddComponent<AlpinePanelGraphic>();
        AlpineUITheme.ApplyPanel(surface);
        surface.radius = 16; surface.borderWidth = 1; surface.raycastTarget = false;
        return surface;
    }

    private static Text Label(string name, Transform parent, string text, Vector2 position, Vector2 size, int fontSize, Color color, bool bold = false, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var rect = Rect(name, parent, size); rect.anchoredPosition = position;
        var label = rect.gameObject.AddComponent<Text>(); label.text = text; label.font = font; label.fontSize = fontSize;
        label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; label.color = color; label.alignment = align; label.raycastTarget = false;
        return label;
    }

    private static Button Action(string name, Transform parent, Vector2 size, Vector2 position, UnityAction action)
    {
        var surface = Panel(name, parent, size, new Vector2(.5f, .5f), position);
        surface.topColor = surface.bottomColor = AlpineUITheme.Inset;
        surface.radius = 9; surface.raycastTarget = true;
        var button = surface.gameObject.AddComponent<Button>(); button.targetGraphic = surface;
        var colors = button.colors; colors.highlightedColor = new Color(.85f, 1, 1); colors.pressedColor = new Color(.65f, .85f, .9f); button.colors = colors;
        UnityEventTools.AddPersistentListener(button.onClick, action);
        return button;
    }

    public static void Inspect()
    {
        EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        var report = new StringBuilder();
        foreach (var t in Object.FindObjectsOfType<Transform>(true).Where(t => t.name == "Rain" || t.name == "Snowfall"))
        {
            report.AppendLine(t.name + " local=" + t.localPosition + " world=" + t.position + " rotation=" + t.eulerAngles + " scale=" + t.lossyScale);
            foreach (var p in t.GetComponentsInChildren<ParticleSystem>(true))
            {
                report.AppendLine(p.name + " world=" + p.transform.position + " rotation=" + p.transform.eulerAngles + " scale=" + p.transform.lossyScale +
                    " local=" + p.transform.localPosition + " speed=" + p.main.startSpeed.constant + " life=" + p.main.startLifetime.constant +
                    " simulation=" + p.main.simulationSpace + " shape=" + p.shape.shapeType + " shapeScale=" + p.shape.scale +
                    " shapeRotation=" + p.shape.rotation + " shapePosition=" + p.shape.position + " rate=" + p.emission.rateOverTime.constant + " max=" + p.main.maxParticles);
                var renderer = p.GetComponent<ParticleSystemRenderer>();
                report.AppendLine(" randomDirection=" + p.shape.randomDirectionAmount + " spherical=" + p.shape.sphericalDirectionAmount +
                    " velocity=" + p.velocityOverLifetime.enabled + " force=" + p.forceOverLifetime.enabled + " noise=" + p.noise.enabled +
                    " rotation=" + p.main.startRotation.constant + " size=" + p.main.startSize.constant +
                    " render=" + renderer.renderMode + " length=" + renderer.lengthScale + " vscale=" + renderer.velocityScale);
            }
            foreach (var component in t.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null) report.AppendLine("SCRIPT " + component.GetType().FullName);
        }
        Directory.CreateDirectory("Logs/WeatherPreview");
        File.WriteAllText("Logs/weather-source-inspection.txt", report.ToString());
    }
}
