using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using Snowboard;
public static class SnowboardIntegrationBuild
{
 const string Base="Assets/LapinerTools/SnowboardStarterKit/";
 public const string ScenePath="Assets/SnowboardIntegration/Snowfield_Integrated.unity";
 public static void Setup()
 {
  var scene=EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
  var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
  var layers=tags.FindProperty("layers"); int terrainLayer=-1;
  for(int i=8;i<32;i++) if(layers.GetArrayElementAtIndex(i).stringValue=="Terrain") terrainLayer=i;
  if(terrainLayer<0) for(int i=8;i<32;i++) if(string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)){terrainLayer=i;layers.GetArrayElementAtIndex(i).stringValue="Terrain";break;}
  var tagList=tags.FindProperty("tags"); foreach(var tag in new[]{"DeadPlayer","Locker"}) {bool exists=false;for(int i=0;i<tagList.arraySize;i++) if(tagList.GetArrayElementAtIndex(i).stringValue==tag)exists=true;if(!exists){tagList.InsertArrayElementAtIndex(tagList.arraySize);tagList.GetArrayElementAtIndex(tagList.arraySize-1).stringValue=tag;}}
  tags.ApplyModifiedPropertiesWithoutUndo();
  var old=GameObject.Find("SnowboardWithCamera"); var oldCtrl=old.GetComponentInChildren<CarvingController>();
  var start=oldCtrl.transform.position; old.SetActive(false);
  foreach(var col in UnityEngine.Object.FindObjectsOfType<Collider>()) if(col.transform.root.name=="主路线" || col is TerrainCollider)col.gameObject.layer=terrainLayer;
  Physics.SyncTransforms();
  var hits=Physics.RaycastAll(new Vector3(start.x,100,start.z),Vector3.down,2000).Where(h=>h.collider.transform.root.name=="主路线").OrderBy(h=>h.distance).ToArray();
  if(hits.Length==0) throw new Exception("No route surface below original player spawn");
  start=hits[0].point+Vector3.up*.65f;
  var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base+"Prefabs/PlayerAlive.prefab"));
  player.name="ImportedSnowboardPlayer"; player.transform.position=start; player.transform.rotation=Quaternion.identity;
  var preview=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base+"Resources/CharacterModels/PlayerModelDefault.prefab"));
  preview.name="EditorPreview";preview.tag="EditorOnly";preview.transform.SetParent(player.transform,false);preview.transform.localPosition=new Vector3(0,-.45f,0);
  var clip=AssetDatabase.LoadAllAssetsAtPath(Base+"Models/Characters/DefaultAvatar/SnowboardAvatar.fbx").OfType<AnimationClip>().First(c=>c.name=="ride");clip.SampleAnimation(preview,.2f);
  foreach(var col in preview.GetComponentsInChildren<Collider>())col.enabled=false;
  player.AddComponent<SnowboardSceneAdapter>().editorPreview=preview;
  var camera=Camera.main; var brain=camera.GetComponent<Cinemachine.CinemachineBrain>();if(brain)brain.enabled=false;
  var follow=camera.gameObject.AddComponent<CameraFollow>();follow.target=player.transform;follow.maxDistance=5.5f;follow.height=2.4f;follow.targetHeight=.5f;
  camera.transform.position=start+new Vector3(0,2.4f,-5.5f);camera.transform.LookAt(start+Vector3.up*.4f);camera.fieldOfView=65;camera.farClipPlane=4000;
  var config=AssetDatabase.LoadAssetAtPath<GameObject>(Base+"Resources/Configs/PlayerConfig.prefab"); var so=new SerializedObject(config.GetComponent<PlayerConfig>());so.FindProperty("m_ALWAYS_ACCELERATE").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
  // The supplied character materials were converted to URP, while this snowfield uses Built-in rendering.
  foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Base+"Models/SharedMaterials"})) {
   var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));if(mat.name.StartsWith("PlayerSnow"))continue;
   var saved=new SerializedObject(mat);var colors=saved.FindProperty("m_SavedProperties.m_Colors");Color color=Color.white;
   for(int i=0;i<colors.arraySize;i++){var item=colors.GetArrayElementAtIndex(i);if(item.FindPropertyRelative("first").stringValue=="_Color")color=item.FindPropertyRelative("second").colorValue;}
   mat.shader=Shader.Find("Standard");mat.color=color;mat.SetFloat("_Glossiness",.2f);EditorUtility.SetDirty(mat);
  }
  Lightmapping.Clear();
  RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.7f,.75f,.8f);RenderSettings.ambientEquatorColor=new Color(.65f,.68f,.72f);RenderSettings.ambientGroundColor=new Color(.4f,.43f,.46f);
  foreach(var light in UnityEngine.Object.FindObjectsOfType<Light>())light.lightmapBakeType=LightmapBakeType.Realtime;
  PrefabUtility.SaveAsPrefabAsset(player,"Assets/SnowboardIntegration/ImportedSnowboardPlayer.prefab");
  EditorSceneManager.SaveScene(scene,ScenePath);
  EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
  PlayerSettings.productName="Snowboard Integration";PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
  AssetDatabase.SaveAssets();
  File.WriteAllText("D:/workspace/SnowboardIntegration/setup-result.txt","Scene: "+ScenePath+"\nSpawn: "+start+"\nGround: "+hits[0].collider.name+"\nAnimation clips: "+string.Join(", ",AssetDatabase.LoadAllAssetsAtPath(Base+"Models/Characters/DefaultAvatar/SnowboardAvatar.fbx").OfType<AnimationClip>().Select(c=>c.name)));
 }
 public static void Build(){var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="D:/workspace/SnowboardIntegration/Windows_2022.3.62f3c1/Snowboard.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});if(r.summary.result!=BuildResult.Succeeded)throw new Exception("Build failed: "+r.summary.result);}
}
