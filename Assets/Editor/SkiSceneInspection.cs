using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SkiSceneInspection
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        var report = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects())
        {
            report.AppendLine("ROOT " + root.name + " " + root.transform.position + " active=" + root.activeSelf);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform || t.name.ToLowerInvariant().Contains("finish") || t.name.Contains("终") ||
                    t.name.Contains("门") || t.name.Contains("旗") || t.name.Contains("点") || t.name.Contains("线"))
                {
                    var renderers = t.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                    {
                        Bounds bounds = renderers[0].bounds;
                        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                        report.AppendLine("  " + t.name + " pos=" + t.position + " rot=" + t.eulerAngles + " bounds=" + bounds);
                    }
                }
            }
        }
        var gear = Object.FindObjectOfType<GearSelect>(true);
        report.AppendLine("SINGLE " + gear.boardSingle.transform.position + " DOUBLE " + gear.boardDouble.transform.position);
        Directory.CreateDirectory("Logs/RacePreview");
        File.WriteAllText("Logs/race-scene-inspection.txt", report.ToString());
        var camera = new GameObject("Inspection Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 900, 250);
        camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        camera.orthographic = true;
        camera.orthographicSize = 650;
        camera.farClipPlane = 3000;
        camera.cullingMask = ~(1 << 5);
        Capture(camera, "Logs/RacePreview/scene-top.png", 1400, 1400);
        Object.DestroyImmediate(camera.gameObject);
        var gate = scene.GetRootGameObjects().First(root => root.name == "BoardShapeModel (1)");
        foreach (var t in gate.GetComponentsInChildren<Transform>(true))
        {
            var r = t.GetComponent<Renderer>();
            report.AppendLine("GATE " + t.name + " pos=" + t.position + " bounds=" + (r != null ? r.bounds.ToString() : "none"));
        }
        Physics.SyncTransforms();
        foreach (float x in new[] { 5f, 20f, 35f, 50f, 65f })
            foreach (var hit in Physics.RaycastAll(new Vector3(x, -1050, 1911), Vector3.down, 250).OrderBy(h => h.distance))
                report.AppendLine("GROUND x=" + x + " " + hit.collider.name + " root=" + hit.collider.transform.root.name + " point=" + hit.point);
        File.WriteAllText("Logs/race-scene-inspection.txt", report.ToString());
        camera = new GameObject("Finish Inspection Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(35, -1112, 1840);
        camera.transform.LookAt(new Vector3(35, -1132, 1911));
        camera.fieldOfView = 65;
        camera.farClipPlane = 4000;
        camera.cullingMask = ~(1 << 5);
        Capture(camera, "Logs/RacePreview/finish-gate.png", 1280, 800);
        Object.DestroyImmediate(camera.gameObject);
    }

    public static void Capture(Camera camera, string path, int width, int height)
    {
        var target = new RenderTexture(width, height, 24);
        var old = camera.targetTexture;
        var oldActive = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        camera.targetTexture = old;
        RenderTexture.active = oldActive;
        Object.DestroyImmediate(texture);
        Object.DestroyImmediate(target);
    }
}
