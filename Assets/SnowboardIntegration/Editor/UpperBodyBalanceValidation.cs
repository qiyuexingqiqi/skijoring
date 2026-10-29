using System;
using System.IO;
using System.Reflection;
using System.Text;
using Snowboard;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch entry: -batchmode -executeMethod UpperBodyBalanceValidation.Run (without -quit).</summary>
[InitializeOnLoad]
public static class UpperBodyBalanceValidation
{
    private const string Pending = "Snowboard.UpperBodyBalanceValidation";
    private const string Finishing = Pending + ".Finishing";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new StringBuilder();
    private static int frames;

    static UpperBodyBalanceValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Finishing, false))
            {
                SessionState.SetBool(Finishing, false);
                EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
                EditorApplication.Exit(SessionState.GetInt(Pending + ".ExitCode", 1));
                return;
            }
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                Time.captureDeltaTime = 1f / 60f;
                EditorApplication.update += WaitForModel;
            }
        };
    }

    public static void Run()
    {
        SessionState.SetBool(Pending, true);
        // A disposable scene avoids changing the snowfield or relying on terrain collisions for pose tests.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SnowboardIntegration/ImportedSnowboardPlayer.prefab");
        UnityEngine.Object.Instantiate(prefab, new Vector3(0f, 10f, 0f), Quaternion.identity);
        EditorApplication.EnterPlaymode();
    }

    private static void WaitForModel()
    {
        if (Time.frameCount < 8 || ++frames < 2) return;
        EditorApplication.update -= WaitForModel;
        int exitCode = 0;
        try { Validate(); }
        catch (Exception error) { Report.AppendLine("FAIL: " + error); exitCode = 1; }
        SessionState.SetBool(Pending, false);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/upper-body-balance-validation.txt", Report.ToString());
        Debug.Log(Report.ToString());
        SessionState.SetInt(Pending + ".ExitCode", exitCode);
        SessionState.SetBool(Finishing, true);
        EditorApplication.ExitPlaymode();
    }

    private sealed class InputDevice : PlayerInputDevice
    {
        public float turn;
        public override float AxisX => turn;
        public override float AxisY => 0f;
        public override bool IsJump => false;
        public override bool IsSlide => false;
        public override bool IsGrab => false;
        public override void Draw() { }
        public override void SaveCurrentAsCalibration() { }
    }

    private static void Validate()
    {
        var player = UnityEngine.Object.FindObjectOfType<PlayerController>();
        var balance = player.GetComponentInChildren<SnowboardUpperBodyBalance>();
        Require(balance != null && balance.enabled, "Runtime model automatically receives balance component");
        var animator = balance.GetComponent<Animator>();
        var spine = (Transform)Get(balance, "spine");
        var chest = (Transform)Get(balance, "chest");
        Require(spine != null && chest != null, "Spine and chest resolved on the supplied rig");
        Report.AppendLine("Rig: " + animator.name + ", humanoid=" + animator.isHuman + ", bones=" + spine.name + "/" + chest.name);

        // Drive the real input/model handlers and Animator deterministically, with physics out of the fixture.
        player.enabled = false;
        balance.enabled = false;
        foreach (var body in player.GetComponentsInChildren<Rigidbody>())
        {
            body.useGravity = false;
            body.detectCollisions = false;
        }
        var input = new InputDevice();
        var devices = (PlayerInputDevice[][])typeof(PlayerInput).GetField("s_devises", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        devices[(int)player.m_playerIndex] = new PlayerInputDevice[] { input };
        var inputHandler = (PlayerInputHandler)Get(player, "m_inputHandler");
        var modelHandler = (PlayerModelHandler)Get(player, "m_modelHandler");
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        float Step(float x, float speed = 8f, PlayerState state = PlayerState.Ground, bool grab = false, int pose = 1)
        {
            Invoke(balance, "Update");
            player.SetState(PlayerState.Ground);
            player.SetGrabState(PlayerGrabState.NO_GRAB);
            player.Velocity = player.BordDirection * speed;
            input.turn = x;
            inputHandler.OnUpdate();
            modelHandler.OnUpdate();
            animator.Update(Mathf.Max(Time.deltaTime, 0.001f));
            player.SetState(state);
            if (grab) player.SetGrabState(PlayerGrabState.RANDOM_GRAB);
            animator.SetInteger("pose", pose);

            var bones = PlayerAnimationHelper.GetAllBones(animator.gameObject);
            var rotations = new Quaternion[bones.Length];
            var positions = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                rotations[i] = bones[i].localRotation;
                positions[i] = bones[i].localPosition;
            }
            Quaternion rootRotation = animator.transform.rotation;
            Vector3 rootPosition = animator.transform.position;
            Quaternion before = chest.rotation;
            Invoke(balance, "LateUpdate");
            Quaternion delta = chest.rotation * Quaternion.Inverse(before);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            float yaw = angle * Vector3.Dot(axis, player.BordUpVector);
            if (float.IsNaN(yaw)) yaw = 0f;

            // The additive pose must not change the board, lower body, or root placement.
            for (int i = 0; i < bones.Length; i++)
            {
                if ((bones[i] == spine || bones[i].IsChildOf(spine))) continue;
                if (Quaternion.Angle(rotations[i], bones[i].localRotation) > 0.05f
                    || Vector3.Distance(positions[i], bones[i].localPosition) > 0.00001f)
                    throw new Exception("Balance changed lower-body/board bone " + bones[i].name);
            }
            if (Quaternion.Angle(rootRotation, animator.transform.rotation) > 0.05f
                || Vector3.Distance(rootPosition, animator.transform.position) > 0.00001f)
                throw new Exception("Balance moved model root");
            var retarget = animator.GetComponent<SnowboardCharacterRetarget>();
            if (retarget != null) retarget.SyncPose();
            var deck = animator.GetComponent<SnowboardDeckRig>();
            if (deck != null) deck.Fit();
            return yaw;
        }

        float yawLeft = 0f;
        for (int i = 0; i < 180; i++) yawLeft = Step(-1f);
        float left = (float)Get(balance, "turn");
        // ProjectNew uses gentler steering (72 deg/s * 0.75 carving = 54 deg/s).
        // Balance should follow the actual turn rate, which is 90% of the 60 deg/s full-pose rate.
        float ExpectedBlend() => Mathf.Clamp01(Mathf.Abs(player.FrameRotY) / Mathf.Max(Time.deltaTime, .0001f)
            / (float)Get(balance, "fullPoseTurnRate"));
        Require(ExpectedBlend() > .5f && Mathf.Abs(left + ExpectedBlend()) < .02f && yawLeft > 15f,
            "Left turn counter-rotates upper body at the configured steering rate: blend=" + left + ", yaw=" + yawLeft);
        Capture(animator, "balance-left");
        Step(1f);
        Require((float)Get(balance, "turn") < 0f, "Direction reversal blends through the previous pose");
        float yawRight = 0f;
        for (int i = 0; i < 180; i++) yawRight = Step(1f);
        Require(ExpectedBlend() > .5f && Mathf.Abs((float)Get(balance, "turn") - ExpectedBlend()) < .02f && yawRight < -15f,
            "Right turn mirrors upper body at the configured steering rate: yaw=" + yawRight);
        Capture(animator, "balance-right");
        Step(0f);
        Require((float)Get(balance, "turn") > 0.1f, "Releasing steering returns smoothly");
        for (int i = 0; i < 240; i++) Step(0f);
        Require(Mathf.Abs((float)Get(balance, "turn")) < 0.001f && !(bool)Get(balance, "poseApplied"), "Straight glide returns to the original animation without drift");
        Capture(animator, "balance-straight");

        for (int i = 0; i < 180; i++) Step(1f, 0f);
        Require(Mathf.Abs((float)Get(balance, "turn")) < 0.001f, "Stationary steering does not produce a full-speed balance pose");
        for (int i = 0; i < 180; i++) Step(1f);
        foreach (var state in new[] { PlayerState.TakeOff, PlayerState.Air, PlayerState.Locked })
        {
            Step(1f, 8f, state);
            Require(!(bool)Get(balance, "poseApplied"), state + " leaves original animation in control");
        }
        Step(1f, 8f, PlayerState.Ground, true);
        Require(!(bool)Get(balance, "poseApplied"), "Grab animation is preserved");
        Step(1f, 8f, PlayerState.Ground, false, (int)EAnim.land_hard);
        Require(!(bool)Get(balance, "poseApplied"), "Landing animation is preserved");

        for (int i = 0; i < 180; i++) Step(1f);
        var spineBase = (Quaternion)Get(balance, "spineAnimation");
        Invoke(balance, "OnDisable");
        Require(Quaternion.Angle(spine.localRotation, spineBase) < 0.05f && !(bool)Get(balance, "poseApplied"), "Disabling balance restores bones before ragdoll handoff");
        Report.AppendLine("PASS: all balance checks, board/lower body unchanged in every sample.");
    }

    private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);

    private static void Capture(Animator animator, string filename)
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.8f, 0.8f, 0.8f);
        var light = new GameObject("Validation Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
        var camera = new GameObject("Validation Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.10f, 0.15f, 0.21f);
        camera.orthographic = true;
        camera.orthographicSize = 1.25f;
        Vector3 target = PlayerAnimationHelper.GetHipsBone(animator.gameObject).position + Vector3.up * 0.15f;
        camera.transform.position = target + animator.transform.TransformDirection(new Vector3(2f, 0.7f, 3.5f));
        camera.transform.LookAt(target);
        var renderTarget = new RenderTexture(640, 640, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = renderTarget;
        camera.Render();
        RenderTexture.active = renderTarget;
        var texture = new Texture2D(640, 640, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
        texture.Apply();
        Directory.CreateDirectory("Logs/BalancePreview");
        File.WriteAllBytes("Logs/BalancePreview/" + filename + ".png", texture.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(camera.gameObject);
        UnityEngine.Object.DestroyImmediate(light.gameObject);
        UnityEngine.Object.DestroyImmediate(renderTarget);
        UnityEngine.Object.DestroyImmediate(texture);
    }
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Report.AppendLine("PASS: " + message);
    }
}
