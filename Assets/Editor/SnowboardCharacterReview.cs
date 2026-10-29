using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Snowboard;

public static class SnowboardCharacterReview
{
    public static void Inspect()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var report = new StringBuilder();
        foreach (var path in new[] {
            "Assets/LapinerTools/SnowboardStarterKit/Resources/CharacterModels/PlayerModelDefault.prefab",
            "Assets/Male_Skier/Prefabs/SK_Male_Skier_A-Pose Variant.prefab" })
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var animator = go.GetComponent<Animator>();
            report.AppendLine("MODEL " + path + " animator=" + animator + " human=" + animator.isHuman + " avatar=" + animator.avatar + " controller=" + AssetDatabase.GetAssetPath(animator.runtimeAnimatorController));
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                report.AppendLine("  RENDERER " + renderer.name + " type=" + renderer.GetType().Name + " bounds=" + renderer.bounds + " mats=" + string.Join(",", renderer.sharedMaterials.Select(m => AssetDatabase.GetAssetPath(m))));
            foreach (HumanBodyBones bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
            {
                var t = animator.isHuman ? animator.GetBoneTransform(bone) : null;
                report.AppendLine("  BONE " + bone + " " + t?.name + " position=" + t?.position);
            }
            if (animator.runtimeAnimatorController != null)
                foreach (var clip in animator.runtimeAnimatorController.animationClips.Distinct())
                    report.AppendLine("  CLIP " + clip.name + " human=" + clip.humanMotion + " " + AssetDatabase.GetAssetPath(clip));
            Object.DestroyImmediate(go);
        }
        Directory.CreateDirectory("Logs/ProjectNew3Review");
        File.WriteAllText("Logs/ProjectNew3Review/character-rigs.txt", report.ToString());
    }

    public static void InspectRemovedObject()
    {
        EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        var report = new StringBuilder();
        var gear = Object.FindObjectOfType<GearSelect>(true);
        foreach (Transform child in gear.boardDouble.transform)
            report.AppendLine("DIRECT CHILD " + child.name + " activeSelf=" + child.gameObject.activeSelf + " renderers=" + child.GetComponentsInChildren<Renderer>(true).Length);
        foreach (var t in gear.boardDouble.GetComponentsInChildren<Transform>(true))
        {
            var inBoard = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(t.gameObject, "Assets/Prefabs/Snowboard.prefab");
            string boardGuid; long boardId;
            if (inBoard != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(inBoard, out boardGuid, out boardId) && boardId == 2775326424733860929)
                report.AppendLine("INCOMING REMOVED OBJECT " + t.name + " activeSelf=" + t.gameObject.activeSelf + " parent=" + t.parent?.name);
            var original = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
            string guid; long id;
            if (original != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out guid, out id))
            {
                if (id == 2775326424733860929 || t.GetComponent<Renderer>() != null)
                    report.AppendLine(t.name + " active=" + t.gameObject.activeSelf + " source=" + AssetDatabase.GetAssetPath(original) + " id=" + id + " enabled=" + t.GetComponent<Renderer>()?.enabled);
            }
        }
        var so = new SerializedObject(Object.FindObjectOfType<CameraViewManager>(true));
        so.FindProperty("minFollowSpeed").floatValue = 1.5f;
        so.FindProperty("minAirFollowSpeed").floatValue = 4f;
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(gear.gameObject.scene);
        File.WriteAllText("Logs/ProjectNew3Review/removed-object.txt", report.ToString());
    }
}
