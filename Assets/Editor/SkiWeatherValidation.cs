using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
public static class SkiWeatherValidation
{
    private const string Key = "Skiing.WeatherValidation";
    private static readonly StringBuilder Report = new StringBuilder();
    private static IEnumerator routine;
    private static Stack<IEnumerator> routines;
    private static int frame = -1;
    private static EquipmentSelectionPointerInput input;

    static SkiWeatherValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                Time.captureDeltaTime = 1f / 60;
                routine = Exercise(); routines = new Stack<IEnumerator>(); routines.Push(routine); frame = -1;
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Exit", false))
            {
                SessionState.SetBool(Key + ".Exit", false);
                EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
                EditorApplication.Exit(SessionState.GetInt(Key + ".Code", 1));
            }
        };
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    public static void BuildAndRun() { SkiWeatherSceneBuilder.Build(); Run(); }

    private static void Tick()
    {
        if (frame == Time.frameCount || Time.frameCount < 20) return;
        frame = Time.frameCount;
        var events = EventSystem.current;
        if (events != null) typeof(EventSystem).GetField("m_HasFocus", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(events, true);
        try
        {
            while (routines.Count > 0)
            {
                var current = routines.Peek();
                if (!current.MoveNext()) { routines.Pop(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; }
                return;
            }
            Finish(0);
        }
        catch (Exception error) { Report.AppendLine("FAIL: " + error); Finish(1); }
    }

    private static IEnumerator Exercise()
    {
        var hud = UnityEngine.Object.FindObjectOfType<SkiGameplayHUD>();
        var weather = hud.weather;
        var race = hud.race;
        var gear = race.gearSelection;
        input = EventSystem.current.gameObject.AddComponent<EquipmentSelectionPointerInput>();
        EventSystem.current.GetComponent<StandaloneInputModule>().inputOverride = input;
        input.position = Vector2.one * -100;
        Require(weather.CurrentWeather == SkiWeather.Sunny && !weather.rainEffect.activeSelf && !weather.snowEffect.activeSelf,
            "Scene starts in sunny weather with both particle effects off");
        Require(hud.weatherPanel.activeInHierarchy && hud.controlsPanel.activeInHierarchy && !hud.speedPanel.activeSelf && SkiGameplayHUD.IsPresent &&
            hud.weatherPanel.transform.IsChildOf(gear.selectUIPanel.transform) && hud.controlsPanel.transform.IsChildOf(gear.selectUIPanel.transform),
            "Weather and controls are both saved inside the equipment selection page");
        Require(hud.controlsText.text.Contains("W /") && hud.controlsText.text.Contains("A / D") && hud.controlsText.text.Contains("Space 单板起跳"),
            "Start page explains WASD, arrows, view switching and the single-board jump");
        Require(gear.selectUIPanel.transform.Find("下雨") == null && gear.selectUIPanel.transform.Find("下雪") == null,
            "Old weather buttons have been removed");
        Capture("start-menu-1280x800", 1280, 800);
        Capture("start-menu-1920x1080", 1920, 1080);
        Capture("start-menu-portrait", 720, 1000);
        yield return Click(hud.weatherButtons[1]);
        CheckWeather(weather, SkiWeather.Snow);
        yield return null;
        Require(weather.snowEffect.GetComponentsInChildren<ParticleSystem>().All(p => p.particleCount > 0), "Snow emits visible particles immediately");
        yield return Click(hud.weatherButtons[2]);
        CheckWeather(weather, SkiWeather.Rain);
        yield return null;
        Require(weather.rainEffect.GetComponent<ParticleSystem>().particleCount > 0, "Rain emits visible particles immediately");
        yield return Click(hud.weatherButtons[0]);
        CheckWeather(weather, SkiWeather.Sunny);
        Require(weather.rainEffect.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0) &&
            weather.snowEffect.GetComponentsInChildren<ParticleSystem>(true).All(p => p.particleCount == 0), "Sunny clears lingering rain and snow particles");

        var audio = UnityEngine.Object.FindObjectOfType<SkiAudioManager>();
        Require((GameObject)typeof(SkiAudioManager).GetField("rainObject", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(audio) == weather.rainEffect &&
            audio.transform.Find("Rain").GetComponent<AudioSource>().enabled, "Rain audio binds to the scene effect, excluding its same-name audio child");
        audio.rainFadeTime = .12f;
        yield return Click(hud.weatherButtons[2]);
        double start = Time.realtimeSinceStartupAsDouble;
        while (Time.realtimeSinceStartupAsDouble - start < .4) yield return null;
        Require(audio.transform.Find("Rain").GetComponent<AudioSource>().volume > .4f, "Rain chosen on the start page fades in weather audio");
        yield return Click(hud.weatherButtons[0]);
        CheckWeather(weather, SkiWeather.Sunny);
        start = Time.realtimeSinceStartupAsDouble;
        while (Time.realtimeSinceStartupAsDouble - start < .4) yield return null;
        Require(audio.transform.Find("Rain").GetComponent<AudioSource>().volume <= .0001f, "Sunny chosen on the start page fades out rain audio");

        foreach (bool dual in new[] { false, true })
        {
            if (dual)
            {
                race.ReturnToSelection();
                for (int i = 0; i < 30; i++) yield return null;
                hud = UnityEngine.Object.FindObjectOfType<SkiGameplayHUD>(); weather = hud.weather; race = hud.race; gear = race.gearSelection;
                input = EventSystem.current.gameObject.AddComponent<EquipmentSelectionPointerInput>();
                EventSystem.current.GetComponent<StandaloneInputModule>().inputOverride = input;
                input.position = Vector2.one * -100;
                Require(hud.weatherPanel.activeInHierarchy && hud.controlsPanel.activeInHierarchy, "Returning from skiing restores the start-page settings");
            }
            var expected = dual ? SkiWeather.Rain : SkiWeather.Snow;
            yield return Click(hud.weatherButtons[(int)expected]);
            CheckWeather(weather, expected);
            var cards = gear.selectUIPanel.GetComponentInChildren<EquipmentSelectionLayout>();
            yield return Click((dual ? cards.doubleCard : cards.singleCard).GetComponent<Button>());
            for (int i = 0; i < 25; i++) yield return null;
            Require(race.IsRunning && !gear.selectUIPanel.activeSelf && !hud.weatherPanel.activeInHierarchy && !hud.controlsPanel.activeInHierarchy &&
                hud.speedPanel.activeSelf && hud.viewPanel.activeSelf && race.timerPanel.activeSelf,
                (dual ? "Double" : "Single") + " board hides setup and controls while keeping speed, timer and camera UI");
            Require(weather.CurrentWeather == expected && (dual ? weather.rainEffect.activeSelf : weather.snowEffect.activeSelf),
                "Starting the selected athlete retains the chosen weather");
            Require(Vector3.Distance(weather.effectsAnchor.position, Camera.main.transform.position) < .02f,
                "Weather follows the rendering camera for " + (dual ? "double" : "single") + " board");
            ValidateSpeedometer(hud, dual);
            Capture(dual ? "ski-double-clean" : "ski-single-clean", 1280, 800);
            int oldView = hud.viewManager.CurrentViewIndex;
            yield return Click(hud.viewPanel.transform.Find("NextView").GetComponent<Button>());
            for (int i = 0; i < 12; i++) yield return null;
            Require(hud.viewManager.CurrentViewIndex == (oldView + 1) % hud.viewManager.ViewCount &&
                Vector3.Distance(weather.effectsAnchor.position, Camera.main.transform.position) < .02f, "Unified camera button changes the view and weather follows it");
            Capture(dual ? "ski-double-portrait" : "ski-single-portrait", 720, 1000);
        }
        // Exercise the completion layer without a long trip down the track; physics is covered by SkiRaceValidation.
        Require(race.TryFinish(race.ActivePlayer.GetComponent<Collider>()), "Current athlete still completes the race with the unified UI");
        yield return null;
        Require(!hud.weatherPanel.activeSelf && race.completionPanel.activeSelf, "Completion card hides gameplay controls and stays above the HUD");
        Capture("start-menu-completion", 1280, 800);
        race.Retry();
        for (int i = 0; i < 30; i++) yield return null;
        hud = UnityEngine.Object.FindObjectOfType<SkiGameplayHUD>();
        Require(hud.race.IsRunning && !hud.weatherPanel.activeInHierarchy && !hud.controlsPanel.activeInHierarchy &&
            hud.weather.CurrentWeather == SkiWeather.Rain && hud.weather.rainEffect.activeSelf,
            "Retry preserves the selected weather while resetting the race clock");
        hud.race.ReturnToSelection();
        for (int i = 0; i < 30; i++) yield return null;
        hud = UnityEngine.Object.FindObjectOfType<SkiGameplayHUD>();
        Require(!hud.race.IsRunning && hud.race.gearSelection.selectUIPanel.activeSelf && hud.weatherPanel.activeInHierarchy &&
            hud.controlsPanel.activeInHierarchy && hud.weather.CurrentWeather == SkiWeather.Rain,
            "Returning to selection retains weather and restores both settings panels");
    }

    private static IEnumerator Click(Button button)
    {
        // A re-enabled selection canvas needs a layout pass before caching pointer coordinates.
        yield return null;
        yield return null;
        Canvas.ForceUpdateCanvases();
        input.position = RectTransformUtility.WorldToScreenPoint(null, button.transform.TransformPoint(((RectTransform)button.transform).rect.center));
        input.down = input.up = input.held = false;
        for (int i = 0; i < 2; i++) yield return null;
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = input.position }, hits);
        Require(hits.Count > 0 && hits[0].gameObject == button.gameObject, button.name + " receives pointer hits above other canvases (" + string.Join(",", hits.Take(3).Select(hit => hit.gameObject.name)) + ")");
        input.down = input.held = true; yield return null;
        input.down = false; yield return null;
        input.held = false; input.up = true; yield return null;
        input.up = false; yield return null;
        yield return null;
    }

    private static void ValidateSpeedometer(SkiGameplayHUD hud, bool dual)
    {
        Require(hud.speedometer != null && !hud.speedometer.raycastTarget,
            "Ice-blue speedometer is bound and does not intercept input");
        var body = hud.race.ActivePlayer.GetComponent<Rigidbody>();
        Vector3 velocity = body.velocity;
        try
        {
            foreach (float kmh in new[] { 0f, 39.6f, 72f })
            {
                body.velocity = Vector3.forward * (kmh / 3.6f);
                hud.Refresh();
                Require(Mathf.Abs(hud.speedometer.SpeedKmh - kmh) < .001f &&
                    Mathf.Abs(float.Parse(hud.speedText.text) - kmh) < .06f &&
                    Mathf.Abs(hud.speedometer.NormalizedSpeed - Mathf.Clamp01(kmh / 60f)) < .001f,
                    (dual ? "Double" : "Single") + " speedometer shows actual " + kmh + " km/h; arc clamps without hiding an over-range speed");
                if (Mathf.Abs(kmh - 39.6f) < .01f)
                    Capture(dual ? "speedometer-double-40" : "speedometer-single-40", 1280, 800);
            }
        }
        finally { body.velocity = velocity; hud.Refresh(); }
        var timer = new Vector3[4]; var speed = new Vector3[4];
        hud.race.timerPanel.GetComponent<RectTransform>().GetWorldCorners(timer);
        hud.speedPanel.GetComponent<RectTransform>().GetWorldCorners(speed);
        Require(timer[1].y < speed[0].y && Mathf.Abs(timer[0].x - speed[0].x) < 1,
            "Full race timer is aligned directly beneath the speedometer, with a visible gap");
    }

    private static void CheckWeather(SkiWeatherController weather, SkiWeather expected)
    {
        Require(weather.CurrentWeather == expected && weather.rainEffect.activeSelf == (expected == SkiWeather.Rain) &&
            weather.snowEffect.activeSelf == (expected == SkiWeather.Snow), "Real pointer click selects mutually exclusive " + expected + " weather");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Report.AppendLine("PASS: " + message);
    }

    private static void Capture(string name, int width, int height)
    {
        var hud = UnityEngine.Object.FindObjectOfType<SkiGameplayHUD>();
        var camera = Camera.main;
        var ui = new GameObject("Weather UI Preview").AddComponent<Camera>();
        ui.CopyFrom(camera); ui.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        ui.clearFlags = CameraClearFlags.Depth; ui.cullingMask = 1 << 5;
        var canvases = new[] { hud.canvasRect.GetComponent<Canvas>(), hud.race.timerPanel.GetComponentInParent<Canvas>(), hud.race.gearSelection.selectUIPanel.GetComponent<Canvas>() };
        var modes = canvases.Select(c => c.renderMode).ToArray();
        var cameras = canvases.Select(c => c.worldCamera).ToArray();
        var scaleModes = canvases.Select(c => c.GetComponent<CanvasScaler>().uiScaleMode).ToArray();
        var scales = canvases.Select(c => c.scaleFactor).ToArray();
        int mask = camera.cullingMask; float aspect = camera.aspect;
        var target = new RenderTexture(width, height, 24);
        camera.targetTexture = target; camera.cullingMask &= ~(1 << 5); camera.aspect = (float)width / height;
        ui.targetTexture = target; ui.aspect = camera.aspect;
        foreach (var canvas in canvases)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = ui; canvas.planeDistance = 1;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = Mathf.Sqrt((float)width / 1280 * height / 800); canvas.scaleFactor = scaler.scaleFactor;
        }
        Canvas.ForceUpdateCanvases();
        hud.Refresh();
        var selectionLayout = hud.race.gearSelection.selectUIPanel.GetComponentInChildren<EquipmentSelectionLayout>();
        if (selectionLayout != null) selectionLayout.Refresh();
        Canvas.ForceUpdateCanvases();
        foreach (var canvas in canvases.Where(c => c.gameObject.activeInHierarchy))
        {
            foreach (var label in canvas.GetComponentsInChildren<Text>())
            {
                foreach (char c in label.text.Where(c => !char.IsWhiteSpace(c)))
                    if (!label.font.HasCharacter(c)) throw new Exception("Missing glyph " + c + " in " + label.name);
                if (label.preferredHeight > label.rectTransform.rect.height + 1 || label.preferredWidth > label.rectTransform.rect.width + 1)
                    throw new Exception("Text clips: " + name + "/" + label.name + " preferred=" + label.preferredWidth + "," + label.preferredHeight + " rect=" + label.rectTransform.rect);
            }
        }
        if (!hud.race.IsFinished)
        {
            var setup = hud.race.gearSelection.selectUIPanel.GetComponentInChildren<EquipmentSelectionLayout>();
            var cardObjects = setup != null ? new[] { setup.singleCard.gameObject, setup.doubleCard.gameObject } : new GameObject[0];
            var panels = new[] { hud.weatherPanel, hud.speedPanel, hud.viewPanel, hud.controlsPanel, hud.race.timerPanel }.Concat(cardObjects).Where(p => p.activeInHierarchy).ToArray();
            var bounds = new List<Rect>();
            foreach (var panel in panels)
            {
                var corners = new Vector3[4]; panel.GetComponent<RectTransform>().GetWorldCorners(corners);
                Vector3 a = ui.WorldToScreenPoint(corners[0]), b = ui.WorldToScreenPoint(corners[2]);
                var rect = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                Require(rect.xMin >= -1 && rect.xMax <= width + 1 && rect.yMin >= -1 && rect.yMax <= height + 1, name + ": " + panel.name + " fits the viewport");
                foreach (var other in bounds) if (rect.Overlaps(other)) throw new Exception(name + " HUD panels overlap");
                bounds.Add(rect);
            }
            foreach (var button in hud.weatherButtons.Where(button => button.gameObject.activeInHierarchy))
            {
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ui.WorldToScreenPoint(button.transform.position) }, hits);
                Require(hits.Count > 0 && hits[0].gameObject == button.gameObject, name + ": weather button stays clickable");
            }
        }
        camera.Render(); ui.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
        Directory.CreateDirectory("Logs/WeatherPreview"); File.WriteAllBytes("Logs/WeatherPreview/" + name + ".png", texture.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null; ui.targetTexture = null;
        camera.cullingMask = mask; camera.aspect = aspect;
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i];
            var scaler = canvases[i].GetComponent<CanvasScaler>(); scaler.uiScaleMode = scaleModes[i]; scaler.scaleFactor = scales[i];
        }
        UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(ui.gameObject);
        Canvas.ForceUpdateCanvases(); hud.Refresh();
        if (selectionLayout != null) selectionLayout.Refresh();
        Require(true, name + ": all text glyphs and layout checked");
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/weather-ui-validation.txt", Report.ToString()); Debug.Log(Report.ToString());
        SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Exit", true); SessionState.SetInt(Key + ".Code", code);
        EditorApplication.ExitPlaymode();
    }
}
