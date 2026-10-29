using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class IntegrationProbe {
 public static void Run() {
  EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
  var sb=new StringBuilder();
  foreach(var m in Object.FindObjectsOfType<MonoBehaviour>(true)) if(m!=null && string.IsNullOrEmpty(m.GetType().Namespace)) sb.AppendLine(m.GetType().Name+" "+m.name+" "+m.transform.position);
  Physics.SyncTransforms();
  foreach(var hit in Physics.RaycastAll(new Vector3(7.56f,100,-29.1f),Vector3.down,3000).OrderBy(h=>h.distance)) sb.AppendLine("HIT "+hit.collider.name+" root="+hit.transform.root.name+" point="+hit.point+" normal="+hit.normal);
  foreach(var r in Object.FindObjectsOfType<Renderer>()) if(r.transform.root.name=="主路线") sb.AppendLine("ROAD "+r.name+" bounds="+r.bounds+" collider="+r.GetComponent<Collider>());
  File.WriteAllText("D:/workspace/SnowboardIntegration/probe.txt",sb.ToString());
  var cam=Camera.main; cam.GetComponent<Cinemachine.CinemachineBrain>().enabled=false;
  cam.transform.position=new Vector3(7.56f,-18,-36); cam.transform.rotation=Quaternion.Euler(12,0,0);
  RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight; RenderSettings.ambientSkyColor=new Color(.7f,.75f,.8f); RenderSettings.ambientEquatorColor=new Color(.65f,.68f,.72f); RenderSettings.ambientGroundColor=new Color(.4f,.43f,.46f);
  var rt=new RenderTexture(1280,720,24);cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes("D:/workspace/SnowboardIntegration/start-probe.png",tex.EncodeToPNG());
 }
}
