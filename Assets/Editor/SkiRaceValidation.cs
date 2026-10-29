using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Play-mode integration checks with real physics trigger entry for both athletes.</summary>
[InitializeOnLoad]
public static class SkiRaceValidation
{
    private const string Key = "Alpine.RaceValidation";
    private static readonly StringBuilder Report = new StringBuilder();
    private static int stage, stageFrame, lastFrame;
    private static double frozenTime;
    private static double stageUnscaled;
    private static GameObject unrelated;
    private static Vector3 finishedPosition;

    static SkiRaceValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                stage = 0; stageFrame = 0; lastFrame = -1;
                Time.captureDeltaTime = 1f / 60f;
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

    private static void Tick()
    {
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            var race = UnityEngine.Object.FindObjectOfType<SkiRaceManager>();
            if (race == null) throw new Exception("Scene race manager missing");
            var gear = race.gearSelection;
            int frames = Time.frameCount - stageFrame;
            switch (stage)
            {
                case 0:
                    if (frames < 24) return;
                    Require(!race.IsRunning && !race.IsFinished && race.ElapsedSeconds == 0 && !race.timerPanel.activeSelf,
                        "Equipment selection does not start the clock");
                    var trigger = UnityEngine.Object.FindObjectOfType<SkiFinishTrigger>();
                    Require(trigger != null && trigger.GetComponent<BoxCollider>().isTrigger && trigger.race == race &&
                        trigger.transform.parent.name == "BoardShapeModel (1)", "Finish trigger is saved under the actual finish arch with its manager reference");
                    Require(SkiRaceManager.FormatTime(3661.239) == "61:01.23", "Race clock formats minutes, seconds and centiseconds without hour wrap");
                    foreach (var label in race.GetComponentsInChildren<Text>(true))
                        foreach (char c in label.text.Where(c => !char.IsWhiteSpace(c)))
                            if (!label.font.HasCharacter(c)) throw new Exception("Missing font glyph " + c + " in " + label.name);
                    Require(true, "Bundled font covers the timer and completion messages");
                    var audio = UnityEngine.Object.FindObjectsOfType<SkiAudioManager>();
                    Require(audio.Length == 1 && audio[0].GetComponentsInChildren<AudioSource>().Length == 3 &&
                        audio[0].GetComponentsInChildren<AudioSource>().All(s => s.clip != null && s.loop), "One audio manager loads all three looping audio clips");
                    ClickEquipment(gear, false);
                    Next(); break;
                case 1:
                    if (frames < 35) return;
                    Require(race.IsRunning && race.ElapsedSeconds > .3 && race.timerPanel.activeSelf &&
                        gear.boardSingle.activeSelf && !gear.boardDouble.activeSelf, "Single-board selection starts the shared clock and timer HUD");
                    Capture(race, "timer-1280x800", 1280, 800);
                    var audioManager = UnityEngine.Object.FindObjectOfType<SkiAudioManager>();
                    typeof(SkiAudioManager).GetMethod("ResolvePlayer", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(audioManager, null);
                    race.ActivePlayer.GetComponent<Rigidbody>().velocity = Vector3.forward * 8;
                    typeof(SkiAudioManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(audioManager, null);
                    Require(audioManager.transform.Find("SnowFriction").GetComponent<AudioSource>().volume > 0 &&
                        audioManager.transform.Find("Wind").GetComponent<AudioSource>().volume > 0, "Moving athlete drives friction and wind volumes");
                    var finish = UnityEngine.Object.FindObjectOfType<SkiFinishTrigger>();
                    unrelated = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    unrelated.name = "Validation non-player";
                    unrelated.transform.position = finish.transform.position;
                    unrelated.AddComponent<Rigidbody>().useGravity = false;
                    Next(); break;
                case 2:
                    if (frames < 15) return;
                    Require(race.IsRunning && !race.IsFinished, "An unrelated rigidbody entering the finish cannot complete the race");
                    UnityEngine.Object.Destroy(unrelated);
                    ApproachFinish(race);
                    Next(); break;
                case 3:
                    if (!race.IsFinished)
                    {
                        if (frames > 240) throw new Exception("Single-board did not enter finish; position=" + race.ActivePlayer.transform.position);
                        return;
                    }
                    CheckFinish(race, false);
                    frozenTime = race.ElapsedSeconds;
                    finishedPosition = race.ActivePlayer.transform.position;
                    Next(); break;
                case 4:
                    if (frames < 45) return;
                    CheckFrozen(race);
                    Capture(race, "finish-single-1280x800", 1280, 800);
                    Capture(race, "finish-single-portrait", 720, 1000);
                    Click(race.retryButton);
                    Next(); break;
                case 5:
                    if (frames < 25) return;
                    Require(race.IsRunning && !race.IsFinished && race.ActivePlayer == gear.boardSingle &&
                        race.ElapsedSeconds < 1.5 && !race.ActivePlayer.GetComponent<Rigidbody>().isKinematic &&
                        !gear.selectUIPanel.activeSelf, "Retry reloads the same equipment with a fresh clock and restored physics");
                    race.ReturnToSelection();
                    Next(); break;
                case 6:
                    if (frames < 25) return;
                    Require(!race.IsRunning && race.ElapsedSeconds == 0 && gear.selectUIPanel.activeSelf &&
                        !gear.boardSingle.activeSelf && !gear.boardDouble.activeSelf, "Returning to selection restores the menu with both athletes inactive");
                    ClickEquipment(gear, true);
                    Next(); break;
                case 7:
                    if (frames < 35) return;
                    Require(race.IsRunning && race.ActivePlayer == gear.boardDouble && race.ElapsedSeconds > .3, "Double-board selection starts the shared clock");
                    ApproachFinish(race);
                    Next(); break;
                case 8:
                    if (!race.IsFinished)
                    {
                        if (frames > 240) throw new Exception("Double-board did not enter finish; position=" + race.ActivePlayer.transform.position);
                        return;
                    }
                    CheckFinish(race, true);
                    frozenTime = race.ElapsedSeconds;
                    finishedPosition = race.ActivePlayer.transform.position;
                    Next(); break;
                case 9:
                    if (frames < 45) return;
                    CheckFrozen(race);
                    Capture(race, "finish-double-1920x1080", 1920, 1080);
                    var manager = UnityEngine.Object.FindObjectOfType<SkiAudioManager>();
                    Require(manager.transform.Find("SnowFriction").GetComponent<AudioSource>().volume == 0 &&
                        manager.transform.Find("Wind").GetComponent<AudioSource>().volume == 0, "Finish stops motion audio along with the athlete");
                    var weather = UnityEngine.Object.FindObjectOfType<SkiWeatherController>();
                    if (weather != null) weather.SelectRain();
                    else Resources.FindObjectsOfTypeAll<GameObject>().First(g => g.name == "Rain" && g.GetComponent<ParticleSystem>() != null && g.scene.IsValid() && g.scene.isLoaded).SetActive(true);
                    manager.rainFadeTime = .15f;
                    Next(); break;
                case 10:
                    if (frames < 30 || Time.realtimeSinceStartupAsDouble - stageUnscaled < .3) return;
                    var rainManager = UnityEngine.Object.FindObjectOfType<SkiAudioManager>();
                    Require(rainManager.transform.Find("Rain").GetComponent<AudioSource>().volume > .4f, "Rain audio fades in when rain weather is enabled");
                    var rainObject = Resources.FindObjectsOfTypeAll<GameObject>().First(g => g.name == "Rain" && g.GetComponent<ParticleSystem>() != null && g.scene.IsValid() && g.scene.isLoaded);
                    Require(rainObject.GetComponentsInChildren<AudioSource>(true).All(a => !a.enabled), "Original rain sources are muted to avoid duplicate weather audio");
                    var clearWeather = UnityEngine.Object.FindObjectOfType<SkiWeatherController>();
                    if (clearWeather != null) clearWeather.SelectSunny();
                    else rainObject.SetActive(false);
                    Next(); break;
                case 11:
                    if (frames < 30 || Time.realtimeSinceStartupAsDouble - stageUnscaled < .3) return;
                    Require(UnityEngine.Object.FindObjectOfType<SkiAudioManager>().transform.Find("Rain").GetComponent<AudioSource>().volume == 0,
                        "Rain audio fades out after switching weather");
                    var back = race.completionPanel.transform.Find("FinishCard/Return").GetComponent<Button>();
                    Click(back);
                    Next(); break;
                case 12:
                    if (frames < 25) return;
                    Require(!race.IsRunning && !race.IsFinished && gear.selectUIPanel.activeSelf &&
                        UnityEngine.Object.FindObjectsOfType<SkiAudioManager>().Length == 1, "Completion return button restores selection without duplicate audio managers");
                    Finish(0); break;
            }
        }
        catch (Exception error) { Report.AppendLine("FAIL: " + error); Finish(1); }
    }

    private static void Next() { stage++; stageFrame = Time.frameCount; stageUnscaled = Time.realtimeSinceStartupAsDouble; }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Report.AppendLine("PASS: " + message);
    }

    private static void ClickEquipment(GearSelect gear, bool dual)
    {
        var layout = gear.selectUIPanel.GetComponentInChildren<EquipmentSelectionLayout>();
        Click((dual ? layout.doubleCard : layout.singleCard).GetComponent<Button>());
    }

    private static void Click(Button button)
    {
        Require(button.onClick.GetPersistentEventCount() == 1, button.name + " has its saved action binding");
        ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }

    private static void ApproachFinish(SkiRaceManager race)
    {
        var trigger = UnityEngine.Object.FindObjectOfType<SkiFinishTrigger>();
        Vector3 target = trigger.transform.position + Vector3.down * 6;
        Vector3 approach = trigger.transform.forward;
        var player = race.ActivePlayer;
        // Place the actual athlete before the gate, then let physics carry its real colliders through.
        // Keep the motion controllers active; disable only gravity to make the crossing repeatable.
        Vector3 delta = target - approach * 10 - player.transform.position;
        var bodies = player.GetComponentsInChildren<Rigidbody>();
        var positions = bodies.Select(body => body.position + delta).ToArray();
        for (int index = 0; index < bodies.Length; index++)
        {
            var body = bodies[index];
            body.position = positions[index];
            body.useGravity = false;
            body.velocity = approach * 11;
            body.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
    }

    private static void CheckFinish(SkiRaceManager race, bool dual)
    {
        Require(race.IsFinished && !race.IsRunning && race.completionPanel.activeSelf && !race.timerPanel.activeSelf,
            (dual ? "Double-board" : "Single-board") + " real physics entry stops the clock and opens completion UI");
        Require(race.ActivePlayer.GetComponentsInChildren<Rigidbody>().All(b => b.isKinematic), "All athlete rigidbodies are stopped");
        Require(dual ? !race.ActivePlayer.GetComponent<CarvingController>().enabled : !race.ActivePlayer.GetComponent<Snowboard.PlayerController>().enabled,
            "Finished athlete no longer accepts movement input");
        Require(race.resultTimeText.text == SkiRaceManager.FormatTime(race.ElapsedSeconds) &&
            race.resultEquipmentText.text.Contains(dual ? "双板" : "单板"), "Result records the final clock and selected equipment");
    }

    private static void CheckFrozen(SkiRaceManager race)
    {
        Require(race.ElapsedSeconds == frozenTime && (race.ActivePlayer.transform.position - finishedPosition).sqrMagnitude < .001f,
            "Final time and athlete position remain fixed across subsequent frames");
        var collider = race.ActivePlayer.GetComponentsInChildren<Collider>().First(c => c.enabled);
        Require(!race.TryFinish(collider) && race.ElapsedSeconds == frozenTime, "Repeated/child collider entries cannot overwrite a completed time");
        Require(Time.timeScale == 1, "Completion keeps normal scene/UI time running");
    }

    private static void Capture(SkiRaceManager race, string name, int width, int height)
    {
        var canvas = race.timerPanel.GetComponentInParent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        var camera = Camera.main;
        var uiCamera = new GameObject("Race Preview UI Camera").AddComponent<Camera>();
        uiCamera.CopyFrom(camera);
        uiCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        uiCamera.clearFlags = CameraClearFlags.Depth;
        uiCamera.cullingMask = 1 << 5;
        var renderMode = canvas.renderMode;
        var worldCamera = canvas.worldCamera;
        var scaleMode = scaler.uiScaleMode;
        float scale = scaler.scaleFactor, aspect = camera.aspect;
        int mask = camera.cullingMask;
        var target = new RenderTexture(width, height, 24);
        camera.targetTexture = target;
        camera.cullingMask &= ~(1 << 5);
        camera.aspect = (float)width / height;
        uiCamera.targetTexture = target;
        uiCamera.aspect = camera.aspect;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
        canvas.planeDistance = 1;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = Mathf.Sqrt((float)width / 1280 * height / 800);
        canvas.scaleFactor = scaler.scaleFactor;
        Canvas.ForceUpdateCanvases();
        foreach (var label in canvas.GetComponentsInChildren<Text>())
            if (label.preferredHeight > label.rectTransform.rect.height + 1 || label.preferredWidth > label.rectTransform.rect.width + 1)
                throw new Exception("Text clipped: " + name + "/" + label.name + " preferred=" + label.preferredWidth + "," + label.preferredHeight + " rect=" + label.rectTransform.rect);
        if (race.IsFinished)
        {
            var card = race.completionPanel.transform.Find("FinishCard").GetComponent<RectTransform>();
            var corners = new Vector3[4]; card.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var point = uiCamera.WorldToScreenPoint(corner);
                if (point.x < 0 || point.x > width || point.y < 0 || point.y > height) throw new Exception("Finish card outside viewport " + name);
            }
            foreach (var button in card.GetComponentsInChildren<Button>())
            {
                var hits = new System.Collections.Generic.List<RaycastResult>();
                canvas.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(EventSystem.current) { position = uiCamera.WorldToScreenPoint(button.transform.position) }, hits);
                Require(hits.Count > 0 && hits[0].gameObject == button.gameObject, name + ": " + button.name + " is clickable above the overlay");
            }
        }
        camera.Render(); uiCamera.Render();
        var active = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
        Directory.CreateDirectory("Logs/RacePreview");
        File.WriteAllBytes("Logs/RacePreview/" + name + ".png", texture.EncodeToPNG());
        RenderTexture.active = active;
        camera.targetTexture = null; uiCamera.targetTexture = null;
        camera.cullingMask = mask; camera.aspect = aspect;
        canvas.renderMode = renderMode; canvas.worldCamera = worldCamera;
        scaler.uiScaleMode = scaleMode; scaler.scaleFactor = scale;
        UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(uiCamera.gameObject);
        Require(true, name + ": UI fits and renders without clipped text");
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        File.WriteAllText("Logs/race-validation.txt", Report.ToString());
        Debug.Log(Report.ToString());
        SessionState.SetBool(Key, false);
        SessionState.SetInt(Key + ".Code", code);
        SessionState.SetBool(Key + ".Exit", true);
        EditorApplication.ExitPlaymode();
    }
}
