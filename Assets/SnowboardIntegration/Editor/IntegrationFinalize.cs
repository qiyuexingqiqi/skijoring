using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class IntegrationFinalize {
 [MenuItem("Tools/Snowboard/Open Integrated Snowfield")]
 public static void Open(){EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);}
 public static void Run(){Open();foreach(var t in Object.FindObjectsOfType<Terrain>()){t.allowAutoConnect=false;t.SetNeighbors(null,null,null,null);}EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();SnowboardIntegrationBuild.Build();}
}
