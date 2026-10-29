using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Snowboard;

[InitializeOnLoad]
public static class SnowboardHeadingValidation
{
    private const string Key = "Skiing.SnowboardHeadingQA";
    private static readonly StringBuilder Report = new StringBuilder();
    private static IEnumerator routine;
    private static int frame = -1;
    static SnowboardHeadingValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                Time.captureDeltaTime = 1f / 60;
                routine = Exercise(); frame = -1;
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
        SnowboardCharacterBuilder.Build();
        ValidatePoseFrames();
        EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }

    private static void ValidatePoseFrames()
    {
        var report = new StringBuilder();
        var model = PrefabUtility.LoadPrefabContents("Assets/LapinerTools/SnowboardStarterKit/Resources/CharacterModels/PlayerModelDefault.prefab");
        var parent = new GameObject("Heading test parent");
        SceneManager.MoveGameObjectToScene(parent, model.scene);
        model.transform.SetParent(parent.transform, true);
        try
        {
            var visual = model.GetComponent<SnowboardCharacterRetarget>();
            var board = model.GetComponent<SnowboardDeckRig>();
            visual.InitializePoseTransfer(); board.Initialize();
            foreach (var parentAngle in new[] { Vector3.zero, new Vector3(11, 37, -9) })
            foreach (float yaw in new[] { 0f, 45f, 90f, 180f, 270f })
            foreach (string clipName in new[] { "ride", "bomb", "air", "jump_regular", "grab01", "land_soft", "land_hard" })
            {
                parent.transform.SetPositionAndRotation(new Vector3(4, 8, -3), Quaternion.Euler(parentAngle));
                visual.source.runtimeAnimatorController.animationClips.First(c => c.name == clipName).SampleAnimation(model, .25f);
                model.transform.localRotation = Quaternion.Euler(17, yaw, -12);
                visual.SyncPose(); board.Fit();
                float error = Vector3.Angle(FeetAxis(visual.source), FeetAxis(visual.clothedRider));
                report.AppendLine($"parent={parentAngle} yaw={yaw} clip={clipName} feetHeadingError={error:F3}");
                if (error > 8f) throw new Exception("Avatar parent-frame conversion failed: " + report);
                foreach (var foot in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    if (Mathf.Abs(Vector3.Dot(visual.clothedRider.GetBoneTransform(foot).position - board.deck.position, board.deck.up) - board.soleOffset) > .005f)
                        throw new Exception("Boot lost contact with the snowboard");
            }
            report.AppendLine("PASS: 70 poses across world headings, parent rotations, lean, riding, jump, grab and landing.");
            visual.ReleasePoseTransfer();
        }
        finally
        {
            model.transform.SetParent(null, true);
            UnityEngine.Object.DestroyImmediate(parent);
            PrefabUtility.UnloadPrefabContents(model);
            Directory.CreateDirectory("Logs/SnowboardHeading");
            File.WriteAllText("Logs/SnowboardHeading/pose-frames.txt", report.ToString());
        }
    }

    private static void Tick()
    {
        if (frame == Time.frameCount || Time.frameCount < 25) return;
        frame = Time.frameCount;
        try { if (!routine.MoveNext()) Finish(0); }
        catch (Exception error) { Report.AppendLine("FAIL: " + error); Finish(1); }
    }

    private static IEnumerator Exercise()
    {
        var gear = UnityEngine.Object.FindObjectOfType<GearSelect>();
        gear.OnClickSingle();
        for (int i = 0; i < 30; i++) yield return null;
        var player = gear.boardSingle.GetComponent<PlayerController>();
        var visual = player.GetComponentInChildren<SnowboardCharacterRetarget>();
        var deck = visual.GetComponent<SnowboardDeckRig>();
        var input = new SnowboardIntegrationQA.TestInput { y = 1 };
        var field = typeof(PlayerInput).GetField("s_devises", BindingFlags.NonPublic | BindingFlags.Static);
        ((PlayerInputDevice[][])field.GetValue(null))[(int)player.m_playerIndex] = new PlayerInputDevice[] { input };
        var start = player.transform.position;
        float maxVisualError = 0, maxPhysicalError = 0;
        foreach (string action in new[] { "straight", "left", "right" })
        {
            input.x = action == "left" ? -.18f : action == "right" ? .18f : 0;
            Vector3 startDirection = player.BordDirection;
            int count = action == "straight" ? 150 : 80;
            int groundFrames = 0;
            for (int i = 0; i < count; i++)
            {
                yield return null;
                float error = Vector3.Angle(FeetAxis(visual.source), FeetAxis(visual.clothedRider));
                maxVisualError = Mathf.Max(maxVisualError, error);
                if (error > 8f) throw new Exception("Visible rider heading diverges during " + action + ": " + error);
                if (Vector3.Angle(deck.deck.right, FeetAxis(visual.clothedRider)) > .1f)
                    throw new Exception("Snowboard did not follow the stance");
                if (player.State == PlayerState.Ground)
                {
                    groundFrames++;
                    float angle = Vector3.Angle(Vector3.ProjectOnPlane(deck.deck.right, player.BordUpVector), player.BordDirection);
                    float axisError = Mathf.Min(angle, 180 - angle);
                    maxPhysicalError = Mathf.Max(maxPhysicalError, axisError);
                    if (axisError > 25f) throw new Exception("Visible board is across the physical heading during " + action + ": " + axisError);
                }
                if (i % 30 == 0)
                    Report.AppendLine($"{action} frame={i} speed={player.Velocity.magnitude:F2} state={player.State} heading={player.BordDirection} source={FeetAxis(visual.source)} suit={FeetAxis(visual.clothedRider)} error={error:F2}");
            }
            float turn = Vector3.SignedAngle(Vector3.ProjectOnPlane(startDirection, Vector3.up), Vector3.ProjectOnPlane(player.BordDirection, Vector3.up), Vector3.up);
            Report.AppendLine($"PASS: {action}, grounded frames={groundFrames}, signed turn={turn:F2} degrees");
            if (groundFrames < 10 || (action != "straight" && Mathf.Abs(turn) < 1)) throw new Exception("Input test did not exercise grounded " + action);
            typeof(SkiWeatherValidation).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { "heading-" + action + "-fixed", 1280, 800 });
        }
        if (Vector3.Distance(start, player.transform.position) < 5) throw new Exception("Athlete did not actually ski");
        if (PlayerConfig.Instance.MAX_SPEED != 11 || gear.boardDouble.GetComponent<CarvingController>().maxSpeed != 11) throw new Exception("Speed preference changed");
        Report.AppendLine($"PASS: real W / W+A / W+D input; distance={Vector3.Distance(start, player.transform.position):F2}m; max avatar heading error={maxVisualError:F3}; max board/physical axis error={maxPhysicalError:F3}");
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory("Logs/SnowboardHeading");
        File.WriteAllText("Logs/SnowboardHeading/skiing-validation.txt", Report.ToString()); Debug.Log(Report.ToString());
        SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".Exit", true); SessionState.SetInt(Key + ".Code", code);
        EditorApplication.ExitPlaymode();
    }

    public static void Inspect()
    {
        var report = new StringBuilder();
        var model = PrefabUtility.LoadPrefabContents("Assets/LapinerTools/SnowboardStarterKit/Resources/CharacterModels/PlayerModelDefault.prefab");
        try
        {
            var retarget = model.GetComponent<SnowboardCharacterRetarget>();
            var deck = model.GetComponent<SnowboardDeckRig>();
            retarget.InitializePoseTransfer(); deck.Initialize();
            foreach (float yaw in new[] { 0f, 45f, 90f, 180f, 270f })
            {
                retarget.source.runtimeAnimatorController.animationClips.First(c => c.name == "ride").SampleAnimation(model, .25f);
                model.transform.rotation = Quaternion.Euler(0, yaw, 0);
                retarget.SyncPose(); deck.Fit();
                report.AppendLine("YAW=" + yaw + " sourceRoot=" + model.transform.eulerAngles + " targetRoot=" + retarget.clothedRider.transform.eulerAngles);
                foreach (var a in new[] { retarget.source, retarget.clothedRider })
                {
                    var l = a.GetBoneTransform(HumanBodyBones.LeftFoot); var r = a.GetBoneTransform(HumanBodyBones.RightFoot);
                    using (var h = new HumanPoseHandler(a.avatar, a.transform))
                    {
                        var p = new HumanPose(); h.GetHumanPose(ref p);
                        report.AppendLine(a.name + " feetAxis=" + (r.position-l.position).normalized.ToString("F3") + " bodyRotation=" + p.bodyRotation.eulerAngles + " hips=" + a.GetBoneTransform(HumanBodyBones.Hips).rotation.eulerAngles);
                    }
                }
                report.AppendLine("deckAxis=" + deck.deck.right.ToString("F3"));
                float error = Vector3.Angle(FeetAxis(retarget.source), FeetAxis(retarget.clothedRider));
                report.AppendLine("headingError=" + error.ToString("F3"));
                if (error > 3f) throw new Exception("Retargeted board heading diverged from source at yaw " + yaw + ": " + error);
                foreach (var t in model.GetComponentsInChildren<Transform>(true).Where(t => t.name == "SnowboardCube"))
                    report.AppendLine("originalBoard=" + t.rotation.eulerAngles + " right=" + t.right + " up=" + t.up + " forward=" + t.forward + " scale=" + t.lossyScale);
            }
            retarget.ReleasePoseTransfer();
        }
        finally { PrefabUtility.UnloadPrefabContents(model); }
        Directory.CreateDirectory("Logs/SnowboardHeading");
        File.WriteAllText("Logs/SnowboardHeading/inspection.txt", report.ToString());
        Debug.Log(report.ToString());
    }
    private static Vector3 FeetAxis(Animator a) => (a.GetBoneTransform(HumanBodyBones.RightFoot).position - a.GetBoneTransform(HumanBodyBones.LeftFoot).position).normalized;
}
