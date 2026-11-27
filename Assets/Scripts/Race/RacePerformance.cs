using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkiRace
{
    // The mountain scene was authored for screenshots: a handful of props are so dense that the
    // frame reached ~500 M triangles on integrated GPUs (159 fir trees at 836 k vertices each).
    // Keep the authored look nearby and stop drawing the heavy props once they are far away.
    public static class RacePerformance
    {
        private const float TreeDistance = 320f;
        private const float BillboardDistance = 120f;
        private const float DetailDistance = 60f;
        private const float DetailDensity = .6f;
        private static float farClip = 1200f;
        private static float heavyRange = 60f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Apply(); }

        private static void Apply()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-raceUnlimited") >= 0) return;
            int quality = 2;
            int index = System.Array.IndexOf(args, "-raceQuality");
            if (index >= 0 && index + 1 < args.Length) int.TryParse(args[index + 1], out quality);
            index = System.Array.IndexOf(args, "-raceFarClip");
            if (index >= 0 && index + 1 < args.Length) float.TryParse(args[index + 1], out farClip);
            index = System.Array.IndexOf(args, "-raceTreeRange");
            if (index >= 0 && index + 1 < args.Length) float.TryParse(args[index + 1], out heavyRange);
            QualitySettings.SetQualityLevel(Mathf.Clamp(quality, 0, QualitySettings.names.Length - 1), true);
            QualitySettings.vSyncCount = 0;
            QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, 60);
            Application.targetFrameRate = 60;

            var camera = Camera.main;
            if (camera != null)
            {
                camera.farClipPlane = Mathf.Min(camera.farClipPlane, farClip);
                camera.allowHDR = false;
                camera.allowMSAA = false;
            }

            foreach (var terrain in Terrain.activeTerrains)
            {
                terrain.treeDistance = Mathf.Min(terrain.treeDistance, TreeDistance);
                terrain.treeBillboardDistance = Mathf.Min(terrain.treeBillboardDistance, BillboardDistance);
                terrain.detailObjectDistance = Mathf.Min(terrain.detailObjectDistance, DetailDistance);
                terrain.detailObjectDensity = Mathf.Min(terrain.detailObjectDensity, DetailDensity);
                terrain.heightmapPixelError = Mathf.Max(terrain.heightmapPixelError, 6);
            }

            var culler = Object.FindObjectOfType<HeavyPropCuller>();
            if (culler == null)
            {
                var go = new GameObject("HeavyPropCuller");
                culler = go.AddComponent<HeavyPropCuller>();
                Object.DontDestroyOnLoad(go);
            }
            culler.Range = heavyRange;
            culler.Rebuild();
            DisableDecorativeColliders();
        }

        /// <summary>
        /// 赛道两侧的护栏网（SM_Prop_Fence）在场景里带了实体碰撞体，常常横在滑行线上：
        /// 玩家撞上去会从 40 km/h 瞬间停住并卡在网前。它们只是装饰，这里统一关掉碰撞。
        /// </summary>
        public static int DisableDecorativeColliders()
        {
            int disabled = 0;
            foreach (var collider in Object.FindObjectsOfType<Collider>(true))
            {
                if (collider.isTrigger || collider is TerrainCollider || !collider.enabled) continue;
                if (!IsTrackDecoration(collider.transform)) continue;
                collider.enabled = false;
                disabled++;
            }
            return disabled;
        }

        private static bool IsTrackDecoration(Transform transform)
        {
            var node = transform;
            for (int depth = 0; node != null && depth < 3; depth++, node = node.parent)
            {
                string name = node.name;
                if (name.StartsWith("SM_Prop_Fence") || name.StartsWith("网")) return true;
            }
            return false;
        }
    }

    // Toggles dense props (photogrammetry trees and rocks) on and off by camera distance.
    public sealed class HeavyPropCuller : MonoBehaviour
    {
        public float Range = 130f;

        private struct Item
        {
            public MeshRenderer renderer;
            public Vector3 position;
            public float radius;
        }

        private Item[] items = new Item[0];
        private float next;

        public void Rebuild()
        {
            var list = new System.Collections.Generic.List<Item>();
            foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount <= 50000) continue;
                list.Add(new Item
                {
                    renderer = renderer,
                    position = renderer.bounds.center,
                    radius = renderer.bounds.extents.magnitude
                });
            }
            items = list.ToArray();
            next = 0;
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (now < next || items.Length == 0) return;
            next = now + .2f;
            var camera = Camera.main;
            if (camera == null) return;
            Vector3 eye = camera.transform.position;
            float limit = Range;
            foreach (var item in items)
            {
                if (item.renderer == null) continue;
                float distance = Vector3.Distance(eye, item.position) - item.radius;
                bool show = distance <= limit;
                if (item.renderer.enabled != show) item.renderer.enabled = show;
            }
        }
    }
}
