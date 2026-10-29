using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Snowboard;

[InitializeOnLoad]
public static class SnowboardClothedValidation
{
    private const string Key = "Skiing.ClothedSnowboardQA";
    private static readonly StringBuilder Report = new StringBuilder();
    private static int stage, startFrame, lastFrame;
    static SnowboardClothedValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                stage = 0; startFrame = 0; lastFrame = -1; Time.captureDeltaTime = 1f / 60;
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
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (lastFrame == Time.frameCount || Time.frameCount < 25) return;
        lastFrame = Time.frameCount;
        try
        {
            var gear = UnityEngine.Object.FindObjectOfType<GearSelect>();
            var player = gear.boardSingle.GetComponent<PlayerController>();
            if (stage == 0) { gear.OnClickSingle(); stage++; startFrame = Time.frameCount; return; }
            if (Time.frameCount - startFrame < 25) return;
            var visual = player.GetComponentInChildren<SnowboardCharacterRetarget>();
            var board = visual.GetComponent<SnowboardDeckRig>();
            Require(visual != null && visual.source.isHuman && visual.clothedRider.isHuman, "Clothed Humanoid visual reuses the existing snowboard motion rig");
            Require(visual.clothedRider.GetComponentsInChildren<Renderer>().Any(r => r.name == "Ski_Suits" && r.enabled), "Winter suit, helmet and boots are rendered");
            Require(visual.clothedRider.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "Skis" || r.name == "Poles").All(r => !r.enabled), "Double skis and ski poles are hidden on the single-board character");
            Require(visual.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.EndsWith("Cube") || r.name.EndsWith("Sphere")).All(r => !r.enabled), "Original geometric mannequin is hidden");
            Vector3 sourceFeet = MidFeet(visual.source), targetFeet = MidFeet(visual.clothedRider);
            Require(Vector3.Distance(sourceFeet, targetFeet) < .005f, "Retargeted feet stay aligned with the original board/physics rig");
            Require(Vector3.Distance(board.deck.position, targetFeet - board.deck.up * board.soleOffset) < .01f, "Rounded snowboard and bindings stay attached to the boots");
            Report.AppendLine("MEASURE: source feet=" + sourceFeet + " clothed feet=" + targetFeet + " rider bounds=" + Bounds(visual.clothedRider.gameObject));
            foreach (string clipName in new[] { "ride", "bomb", "air", "jump_regular", "grab01", "land_soft", "land_hard" })
            {
                var clip = visual.source.runtimeAnimatorController.animationClips.First(c => c.name == clipName);
                clip.SampleAnimation(visual.source.gameObject, Mathf.Min(.3f, clip.length * .5f));
                visual.SyncPose(); board.Fit();
                foreach (var foot in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    float height = Vector3.Dot(visual.clothedRider.GetBoneTransform(foot).position - board.deck.position, board.deck.up);
                    Require(Mathf.Abs(height - board.soleOffset) < .005f, clipName + ": both boots stay on the snowboard plane");
                }
            }
            visual.source.runtimeAnimatorController.animationClips.First(c => c.name == "ride").SampleAnimation(visual.source.gameObject, .3f);
            visual.SyncPose(); board.Fit();
            var targetChest = visual.clothedRider.GetBoneTransform(HumanBodyBones.Chest);
            var sourceChest = visual.source.GetBoneTransform(HumanBodyBones.Chest);
            Quaternion beforeChest = targetChest.rotation;
            Quaternion sourceChestPose = sourceChest.localRotation;
            sourceChest.rotation = Quaternion.AngleAxis(18f, visual.transform.up) * sourceChest.rotation;
            visual.SyncPose();
            Require(Quaternion.Angle(beforeChest, targetChest.rotation) > 8f, "The clothed torso follows the original additive upper-body turning pose");
            sourceChest.localRotation = sourceChestPose;
            visual.SyncPose(); board.Fit();
            Capture(visual, "clothed-riding", new Vector3(2.6f, 1.1f, -3.8f));
            Capture(visual, "clothed-front", new Vector3(-2.6f, 1.1f, 3.8f));
            Capture(visual, "deck-detail", new Vector3(1.6f, .9f, -1.6f), true);
            Require(PlayerConfig.Instance.MAX_SPEED == 11f && gear.boardDouble.GetComponent<CarvingController>().maxSpeed == 11f, "Both equipment types retain the requested 39.6 km/h speed limit");
            ValidateCamera(gear, player);
            Finish(0);
        }
        catch (Exception error) { Report.AppendLine("FAIL: " + error); Finish(1); }
    }
    private static Vector3 MidFeet(Animator a) => (a.GetBoneTransform(HumanBodyBones.LeftFoot).position + a.GetBoneTransform(HumanBodyBones.RightFoot).position) * .5f;
    private static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        var result = renderers[0].bounds; foreach (var r in renderers) result.Encapsulate(r.bounds); return result;
    }
    private static void ValidateCamera(GearSelect gear, PlayerController single)
    {
        var manager = UnityEngine.Object.FindObjectOfType<CameraViewManager>();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var method = typeof(CameraViewManager).GetMethod("UpdateRig", flags);
        var rigField = typeof(CameraViewManager).GetField("rig", flags);
        var lastForward = typeof(CameraViewManager).GetField("lastFlatForward", flags);
        foreach (bool dual in new[] { false, true })
        {
            var obj = dual ? gear.boardDouble : gear.boardSingle;
            var carving = dual ? obj.GetComponent<CarvingController>() : null;
            if (carving != null) typeof(CarvingController).GetField("isGrounded", flags).SetValue(carving, true);
            single.SetState(PlayerState.Ground);
            obj.transform.rotation = Quaternion.identity;
            if (!dual) typeof(PlayerController).GetField("m_bordDirection", flags).SetValue(single, Vector3.forward);
            manager.Bind(obj.transform, carving, dual ? null : single);
            var rig = (Transform)rigField.GetValue(manager);
            var body = obj.GetComponent<Rigidbody>();
            rig.rotation = Quaternion.identity; lastForward.SetValue(manager, Vector3.forward);
            body.velocity = Vector3.back * 8;
            for (int i = 0; i < 30; i++) method.Invoke(manager, null);
            Require(Quaternion.Angle(rig.rotation, Quaternion.identity) < .1f, (dual ? "Double" : "Single") + " camera holds heading during a backward rebound");
            body.velocity = Vector3.right * .2f;
            obj.transform.rotation = Quaternion.Euler(0, 90, 0);
            if (!dual) typeof(PlayerController).GetField("m_bordDirection", flags).SetValue(single, Vector3.right);
            for (int i = 0; i < 30; i++) method.Invoke(manager, null);
            Require(Quaternion.Angle(rig.rotation, Quaternion.identity) < .1f, "Camera does not flip due to low-speed board jitter");
            body.velocity = Vector3.right * 8;
            method.Invoke(manager, null);
            float turn = Quaternion.Angle(rig.rotation, Quaternion.identity);
            Require(turn > 0 && turn <= 180f * Time.deltaTime + .1f, "Camera resumes forward following with a bounded turn rate");
            if (carving != null) typeof(CarvingController).GetField("isGrounded", flags).SetValue(carving, false);
            else single.SetState(PlayerState.Air);
            rig.rotation = Quaternion.identity; lastForward.SetValue(manager, Vector3.forward);
            body.velocity = Vector3.right * 2;
            method.Invoke(manager, null);
            Require(Quaternion.Angle(rig.rotation, Quaternion.identity) < .1f, "Camera holds its heading near a low-speed jump apex");
            body.velocity = Vector3.right * 8;
            method.Invoke(manager, null);
            Require(Quaternion.Angle(rig.rotation, Quaternion.identity) <= 72f * Time.deltaTime + .1f, "Airborne camera following is limited to 40 percent yaw speed");
        }
        manager.Bind(single.transform, null, single);
    }
    private static void Capture(SnowboardCharacterRetarget visual, string name, Vector3 offset, bool deckOnly = false)
    {
        var camera = new GameObject("Character QA Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.17f, .24f, .31f);
        camera.cullingMask = 1 << 31; camera.nearClipPlane = .01f;
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        var layers = renderers.Select(r => r.gameObject.layer).ToArray();
        foreach (var renderer in renderers) renderer.gameObject.layer = 31;
        camera.orthographic = true; camera.orthographicSize = deckOnly ? .85f : 1.15f;
        Vector3 target = deckOnly ? visual.GetComponent<SnowboardDeckRig>().deck.position : MidFeet(visual.clothedRider) + visual.transform.up * .65f;
        camera.transform.position = target + visual.transform.TransformDirection(offset); camera.transform.LookAt(target, visual.transform.up);
        Directory.CreateDirectory("Logs/ProjectNew3Review/Preview");
        SkiSceneInspection.Capture(camera, "Logs/ProjectNew3Review/Preview/" + name + ".png", 900, 900);
        for (int i = 0; i < renderers.Length; i++) renderers[i].gameObject.layer = layers[i];
        UnityEngine.Object.DestroyImmediate(camera.gameObject);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message); Report.AppendLine("PASS: " + message);
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick; File.WriteAllText("Logs/ProjectNew3Review/clothed-and-camera-validation.txt", Report.ToString()); Debug.Log(Report.ToString());
        SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Exit", true); SessionState.SetInt(Key + ".Code", code); EditorApplication.ExitPlaymode();
    }
}
