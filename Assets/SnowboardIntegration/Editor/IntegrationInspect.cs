using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class IntegrationInspect {
 public static void Run() {
  var sb=new StringBuilder();
  foreach(var path in new[]{"Assets/Scenes/SampleScene.unity","Assets/Scenes/1A+1B.unity","Assets/雪场景/Scenes/SkiRaceTrack_v5_roadExport.unity"}) {
   var scene=EditorSceneManager.OpenScene(path);
   sb.AppendLine("SCENE "+path);
   foreach(var root in scene.GetRootGameObjects()) sb.AppendLine("ROOT "+root.name+" pos="+root.transform.position+" active="+root.activeSelf);
   foreach(var c in UnityEngine.Object.FindObjectsOfType<Camera>(true)) {
    sb.AppendLine("CAM "+c.name+" pos="+c.transform.position+" rot="+c.transform.eulerAngles+" active="+c.gameObject.activeInHierarchy);
    if(c.enabled && c.gameObject.activeInHierarchy) {var rt=new RenderTexture(1280,720,24); c.targetTexture=rt; c.Render(); RenderTexture.active=rt; var tex=new Texture2D(1280,720,TextureFormat.RGB24,false); tex.ReadPixels(new Rect(0,0,1280,720),0,0); tex.Apply(); File.WriteAllBytes("D:/workspace/SnowboardIntegration/"+Path.GetFileNameWithoutExtension(path)+"-"+c.name.Replace("/","_")+".png",tex.EncodeToPNG()); c.targetTexture=null; RenderTexture.active=null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);}
   }
   foreach(var m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true)) if(m!=null && !m.GetType().Namespace?.StartsWith("Unity")==true) sb.AppendLine("SCRIPT "+m.GetType().FullName+" on "+m.name+" pos="+m.transform.position);
  }
  File.WriteAllText("D:/workspace/SnowboardIntegration/scene-inspection.txt",sb.ToString());
 }
}
