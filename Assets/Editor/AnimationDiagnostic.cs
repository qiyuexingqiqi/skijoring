#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

public static class AnimationDiagnostic
{
    [MenuItem("Tools/Skiing/Diagnose Animation Issue")]
    public static void Diagnose()
    {
        GameObject skier = GameObject.Find("character 1");
        if (skier == null) skier = GameObject.Find("SkierModel");
        if (skier == null)
        {
            Debug.LogError("[AnimDiag] Cannot find character 1 or SkierModel!");
            return;
        }

        Animator animator = skier.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("[AnimDiag] No Animator on " + skier.name);
            return;
        }

        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null)
        {
            Debug.LogError("[AnimDiag] No controller!");
            return;
        }

        Debug.Log("=== Animation Diagnostic ===");
        Debug.Log("Object: " + skier.name);
        string avatarInfo = (animator.avatar != null) ? animator.avatar.isHuman.ToString() : "NONE";
        Debug.Log("Avatar: " + avatarInfo);
        Debug.Log("Controller: " + controller.name);

        int layerCount = controller.layers.Length;
        for (int l = 0; l < layerCount; l++)
        {
            var layer = controller.layers[l];
            if (layer.stateMachine == null) continue;

            ChildAnimatorState[] states = layer.stateMachine.states;
            int stateCount = states.Length;
            for (int s = 0; s < stateCount; s++)
            {
                var state = states[s];
                Debug.Log("State: " + state.state.name);

                BlendTree bt = state.state.motion as BlendTree;
                if (bt != null)
                {
                    Debug.Log("  BlendParam: " + bt.blendParameter + " Children: " + bt.children.Length);

                    ChildMotion[] children = bt.children;
                    int childCount = children.Length;
                    for (int i = 0; i < childCount; i++)
                    {
                        var child = children[i];
                        AnimationClip clip = child.motion as AnimationClip;
                        bool valid = (child.motion != null && clip != null);
                        string motionName = (child.motion != null) ? child.motion.GetType().Name : "NULL";
                        Debug.Log("    [" + i + "] threshold=" + child.threshold.ToString("F1") + " motion=" + motionName + " valid=" + valid);

                        if (clip != null)
                        {
                            EditorCurveBinding[] curves = AnimationUtility.GetCurveBindings(clip);
                            int curveCount = curves.Length;
                            Debug.Log("       clip=" + clip.name + " length=" + clip.length.ToString("F2") + "s curves=" + curveCount);
                            if (curveCount == 0)
                                Debug.LogError("       !!! CLIP HAS NO CURVE BINDINGS !!!");
                        }
                        else
                            Debug.LogError("       !!! MOTION IS NULL !!!");
                    }
                }
            }
        }

        int ti = -1;
        int bi = -1;
        AnimatorControllerParameter[] parameters = controller.parameters;
        int paramCount = parameters.Length;
        for (int i = 0; i < paramCount; i++)
        {
            if (parameters[i].name == "turnInput") ti = i;
            if (parameters[i].name == "Blend") bi = i;
        }
        string tiStr = (ti >= 0) ? "FOUND" : "MISSING";
        string biStr = (bi >= 0) ? "FOUND" : "MISSING";
        Debug.Log("Parameters: turnInput=" + tiStr + ", Blend=" + biStr);
        Debug.Log("=== End Diagnostic ===");
    }

    [MenuItem("Tools/Skiing/Fix Animation - Reimport FBX")]
    public static void FixFBXImport()
    {
        string[] fbxPaths = new string[]
        {
            "Assets/Animation/X Bot@Idle.fbx",
            "Assets/Animation/passive_marker_man@Left Turn.fbx",
            "Assets/Animation/passive_marker_man@Right Turn.fbx"
        };

        for (int idx = 0; idx < fbxPaths.Length; idx++)
        {
            string path = fbxPaths[idx];
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[AnimFix] Not found: " + path);
                continue;
            }

            importer.importAnimation = true;
            importer.importVisibility = true;
            importer.optimizeGameObjects = false;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path).Replace("@", "_").Replace(" ", "");
                ModelImporterClipAnimation defaultClip = new ModelImporterClipAnimation();
                defaultClip.name = name;
                defaultClip.takeName = "Mixamo";
                defaultClip.firstFrame = 1;
                defaultClip.lastFrame = 60;
                defaultClip.loop = true;
                defaultClip.wrapMode = WrapMode.Loop;
                importer.clipAnimations = new ModelImporterClipAnimation[] { defaultClip };
                Debug.Log("[AnimFix] Created default clip '" + name + "'");
            }
            else
            {
                for (int i = 0; i < clips.Length; i++)
                    Debug.Log("[AnimFix] clip[" + i + "]='" + clips[i].name + "' frames=[" + clips[i].firstFrame + "-" + clips[i].lastFrame + "]");
            }

            importer.SaveAndReimport();
            Debug.Log("[AnimFix] Reimported: " + path);
        }

        AssetDatabase.Refresh();

        ModelImporter charImporter = AssetImporter.GetAtPath("Assets/character.fbx") as ModelImporter;
        if (charImporter != null)
        {
            charImporter.importAnimation = true;
            charImporter.importVisibility = true;
            charImporter.SaveAndReimport();
            Debug.Log("[AnimFix] Reimported: Assets/character.fbx");
        }

        Debug.Log("[AnimFix] Done! Run Tools/Skiing/Diagnose Animation Issue to verify.");
    }

    [MenuItem("Tools/Skiing/Fix Animation - Check Clips")]
    public static void CheckClips()
    {
        string[] paths = new string[]
        {
            "Assets/Animation/X Bot@Idle.fbx",
            "Assets/Animation/passive_marker_man@Left Turn.fbx",
            "Assets/Animation/passive_marker_man@Right Turn.fbx"
        };

        for (int p = 0; p < paths.Length; p++)
        {
            Object[] objs = AssetDatabase.LoadAllAssetRepresentationsAtPath(paths[p]);
            bool foundAny = false;
            for (int o = 0; o < objs.Length; o++)
            {
                AnimationClip clip = objs[o] as AnimationClip;
                if (clip != null)
                {
                    EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                    Debug.Log("[ClipCheck] " + paths[p] + " -> " + clip.name + " length=" + clip.length.ToString("F2") + "s bindings=" + bindings.Length);
                    foundAny = true;
                }
            }
            if (!foundAny) Debug.LogWarning("[ClipCheck] " + paths[p] + " has ZERO sub-assets!");
        }
    }
}
#endif