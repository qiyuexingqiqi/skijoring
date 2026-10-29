using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Batch entry: -batchmode -executeMethod SkierTurnBalanceValidation.Run (without -quit).</summary>
[InitializeOnLoad]
public static class SkierTurnBalanceValidation
{
    private const string Key = "Skiing.TurnBalanceValidation";
    private const string ModelPath = "Assets/Male_Skier/Prefabs/SK_Male_Skier_A-Pose Variant.prefab";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder Report = new StringBuilder();

    static SkierTurnBalanceValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                Time.captureDeltaTime = 1f / 60f;
                EditorApplication.update += WaitForModel;
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Finish", false))
            {
                SessionState.SetBool(Key + ".Finish", false);
                EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
                EditorApplication.Exit(SessionState.GetInt(Key + ".ExitCode", 1));
            }
        };
    }

    public static void Run()
    {
        // Keep the parameters editable on the same model prefab used by the double-board UI option.
        var model = PrefabUtility.LoadPrefabContents(ModelPath);
        if (model.GetComponent<SkierTurnBalance>() == null)
        {
            model.AddComponent<SkierTurnBalance>();
            PrefabUtility.SaveAsPrefabAsset(model, ModelPath);
        }
        PrefabUtility.UnloadPrefabContents(model);

        var source = EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        var selection = UnityEngine.Object.FindObjectOfType<GearSelect>(true);
        if (selection == null || selection.boardDouble == null) throw new Exception("Double-board selection is not wired");
        var clone = UnityEngine.Object.Instantiate(selection.boardDouble);
        clone.name = "DoubleBoardValidation";
        var fixture = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.MoveGameObjectToScene(clone, fixture);
        SceneManager.SetActiveScene(fixture);
        EditorSceneManager.CloseScene(source, true);
        clone.transform.position = new Vector3(0f, 10f, 0f);
        clone.transform.rotation = Quaternion.identity;
        clone.SetActive(true);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    private static void WaitForModel()
    {
        if (Time.frameCount < 8) return;
        EditorApplication.update -= WaitForModel;
        int code = 0;
        try { Validate(); }
        catch (Exception error) { Report.AppendLine("FAIL: " + error); code = 1; }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/double-board-balance-validation.txt", Report.ToString());
        Debug.Log(Report.ToString());
        SessionState.SetBool(Key, false);
        SessionState.SetInt(Key + ".ExitCode", code);
        SessionState.SetBool(Key + ".Finish", true);
        EditorApplication.ExitPlaymode();
    }

    private static void Validate()
    {
        var movement = UnityEngine.Object.FindObjectOfType<CarvingController>();
        Require(movement != null, "UI double-board object uses CarvingController");
        var animator = movement.skierAnimator;
        var balance = animator.GetComponent<SkierTurnBalance>();
        Require(balance != null && balance.enabled, "Controller initializes the double-board balance component");
        var spine = (Transform)Get(balance, "spine");
        var chest = (Transform)Get(balance, "chest");
        Require(spine != null && chest != null, "Actual double-board spine/chest mapping resolves");
        Report.AppendLine("Model=" + animator.name + ", controller=" + animator.runtimeAnimatorController.name
            + ", bones=" + spine.name + "/" + chest.name);

        balance.enabled = false;
        foreach (var body in movement.GetComponentsInChildren<Rigidbody>())
        {
            body.useGravity = false;
            body.detectCollisions = false;
            body.velocity = Vector3.zero;
        }
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Play("Male_Skier.Anim_Male_Skier_Skiing_Straight", 0, 0.25f);
        animator.Update(0f);
        Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Anim_Male_Skier_Skiing_Straight"), "Original skiing animation plays on the supplied avatar");

        float Step(float steering, float speed = 9f, bool grounded = true)
        {
            Invoke(balance, "Update");
            Set(movement, "currentAnimTurnInput", steering);
            Set(movement, "currentSpeed", speed);
            Set(movement, "isGrounded", grounded);
            animator.Update(1f / 60f);
            var bones = animator.GetComponentsInChildren<Transform>();
            var rotations = bones.Select(b => b.localRotation).ToArray();
            var positions = bones.Select(b => b.localPosition).ToArray();
            Quaternion before = chest.rotation;
            Invoke(balance, "LateUpdate");
            var delta = chest.rotation * Quaternion.Inverse(before);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            float yaw = angle * Vector3.Dot(axis, movement.transform.up);
            if (float.IsNaN(yaw)) yaw = 0f;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == spine || bones[i].IsChildOf(spine)) continue;
                if (Quaternion.Angle(rotations[i], bones[i].localRotation) > 0.05f
                    || Vector3.Distance(positions[i], bones[i].localPosition) > 0.00001f)
                    throw new Exception("Pose moved feet/skis/root: " + bones[i].name);
            }
            return yaw;
        }

        float yawLeft = 0f;
        for (int i = 0; i < 180; i++) yawLeft = Step(-1f);
        Require((float)Get(balance, "turn") < -0.99f && yawLeft > 10f, "Left turn counter-rotates chest: " + yawLeft.ToString("F2") + " degrees");
        Capture(animator, "double-left");
        Step(1f);
        Require((float)Get(balance, "turn") < 0f, "Reversal passes smoothly through the previous pose");
        float yawRight = 0f;
        for (int i = 0; i < 180; i++) yawRight = Step(1f);
        Require((float)Get(balance, "turn") > 0.99f && yawRight < -10f, "Right turn mirrors chest: " + yawRight.ToString("F2") + " degrees");
        Capture(animator, "double-right");
        Step(0f);
        Require((float)Get(balance, "turn") > 0.1f, "Release blends back instead of snapping");
        for (int i = 0; i < 240; i++) Step(0f);
        Require(Mathf.Abs((float)Get(balance, "turn")) < 0.001f && !(bool)Get(balance, "poseApplied"), "Straight glide restores the original pose without accumulated rotation");
        Capture(animator, "double-straight");
        for (int i = 0; i < 180; i++) Step(1f, 4f);
        float slowTurn = (float)Get(balance, "turn");
        Require(slowTurn > 0.3f && slowTurn < 0.5f, "Low speed reduces the balance amplitude");
        for (int i = 0; i < 180; i++) Step(1f, 0f);
        Require(Mathf.Abs((float)Get(balance, "turn")) < 0.001f, "Stationary input does not lean the skier");
        for (int i = 0; i < 180; i++) Step(1f);
        Step(1f, 9f, false);
        Require(!(bool)Get(balance, "poseApplied"), "Airborne animation retains full control");
        foreach (string special in new[] { "Braking", "Braking_Loop", "Fall", "Losing", "Winning" })
        {
            animator.Play("Male_Skier.Anim_Male_Skier_" + special, 0, 0.1f);
            animator.Update(0f);
            Step(1f);
            Require(!(bool)Get(balance, "poseApplied"), special + " animation is preserved");
        }
        animator.Play("Male_Skier.Anim_Male_Skier_Skiing_Straight", 0, 0.25f);
        animator.Update(0f);
        for (int i = 0; i < 180; i++) Step(1f);
        var original = (Quaternion)Get(balance, "spineAnimation");
        Invoke(balance, "OnDisable");
        Require(Quaternion.Angle(spine.localRotation, original) < 0.05f && (float)Get(balance, "turn") == 0f,
            "Switching off the character resets the pose and smoothing");
        Report.AppendLine("PASS: all double-board balance checks; feet/skis/root unchanged in every sample.");
    }

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
        camera.orthographicSize = 1.6f;
        Vector3 target = animator.GetBoneTransform(HumanBodyBones.Hips).position;
        camera.transform.position = target + animator.transform.TransformDirection(new Vector3(2f, 0.5f, 3.5f));
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

    private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Report.AppendLine("PASS: " + message);
    }
}
