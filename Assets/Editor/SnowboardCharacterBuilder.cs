using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Snowboard;

public static class SnowboardCharacterBuilder
{
    private const string ModelPath = "Assets/LapinerTools/SnowboardStarterKit/Resources/CharacterModels/PlayerModelDefault.prefab";
    private const string EquipmentFolder = "Assets/SnowboardIntegration/Equipment";

    [MenuItem("Tools/Skiing/Build Clothed Snowboard Rider")]
    public static void Build()
    {
        Directory.CreateDirectory(EquipmentFolder);
        AssetDatabase.Refresh();
        var model = PrefabUtility.LoadPrefabContents(ModelPath);
        try
        {
            foreach (var name in new[] { "ClothedSnowboardRider", "AlpineSnowboard" })
            {
                var old = model.transform.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = false;
            var originalAnimator = model.GetComponent<Animator>();
            var male = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Male_Skier/Prefabs/SK_Male_Skier_A-Pose Variant.prefab"), model.transform);
            male.name = "ClothedSnowboardRider";
            foreach (var balance in male.GetComponentsInChildren<SkierTurnBalance>(true)) Object.DestroyImmediate(balance);
            var animator = male.GetComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            male.transform.localPosition = Vector3.zero;
            male.transform.localRotation = Quaternion.identity;
            male.transform.localScale = Vector3.one * (originalAnimator.humanScale / animator.humanScale);
            foreach (var renderer in male.GetComponentsInChildren<Renderer>(true))
                if (renderer.name == "Skis" || renderer.name == "Poles") renderer.enabled = false;
            var retarget = model.GetComponent<SnowboardCharacterRetarget>();
            if (retarget == null) retarget = model.AddComponent<SnowboardCharacterRetarget>();
            retarget.source = originalAnimator;
            retarget.clothedRider = animator;

            var top = Material("GlacierTopsheet", new Color(.08f, .53f, .73f), .2f);
            var baseMaterial = Material("CarbonBase", new Color(.035f, .065f, .09f), .1f);
            var edge = Material("SteelEdges", new Color(.6f, .71f, .77f), .8f);
            var bindingMaterial = Material("Bindings", new Color(.045f, .10f, .16f), .1f);
            var accent = Material("DeckAccent", new Color(.58f, .95f, .83f), .1f);
            var white = Material("DeckWhite", new Color(.94f, .98f, 1f), .05f);
            var board = new GameObject("AlpineSnowboard");
            board.transform.SetParent(model.transform, false);
            var mesh = CreateDeck();
            string meshPath = EquipmentFolder + "/AlpineFreerideDeck.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, meshPath);
            board.AddComponent<MeshFilter>().sharedMesh = mesh;
            board.AddComponent<MeshRenderer>().sharedMaterials = new[] { top, baseMaterial, edge };
            var deckRig = model.GetComponent<SnowboardDeckRig>();
            if (deckRig == null) deckRig = model.AddComponent<SnowboardDeckRig>();
            deckRig.animator = animator; deckRig.deck = board.transform;
            deckRig.leftBinding = Binding("LeftBinding", board.transform, bindingMaterial, accent);
            deckRig.rightBinding = Binding("RightBinding", board.transform, bindingMaterial, accent);
            // Topsheet graphics are separate editable geometry, with no external image dependency.
            for (int i = 0; i < 3; i++)
            {
                var stripe = Part("NoseStripe" + i, board.transform, new Vector3(.57f + i * .075f, .027f, 0), new Vector3(.028f, .004f, .24f), i == 1 ? white : accent);
                stripe.transform.localRotation = Quaternion.Euler(0, -24, 0);
            }
            for (int i = 0; i < 2; i++)
                Part("TailStripe" + i, board.transform, new Vector3(-.64f + i * .055f, .031f, 0), new Vector3(.018f, .004f, .23f), white);
            Part("CenterRail", board.transform, new Vector3(0, .013f, .13f), new Vector3(.88f, .004f, .008f), accent);
            var controller = originalAnimator.runtimeAnimatorController;
            var ride = controller.animationClips.First(clip => clip.name == "ride");
            ride.SampleAnimation(model, .25f);
            retarget.InitializePoseTransfer(); retarget.SyncPose();
            deckRig.Initialize(); deckRig.Fit();
            retarget.ReleasePoseTransfer();
            PrefabUtility.SaveAsPrefabAsset(model, ModelPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(model); }
        AssetDatabase.SaveAssets();
    }

    private static Material Material(string name, Color color, float metallic)
    {
        string path = EquipmentFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
        material.color = color; material.SetFloat("_Metallic", metallic); material.SetFloat("_Glossiness", .36f); EditorUtility.SetDirty(material);
        return material;
    }

    private static Transform Binding(string name, Transform parent, Material dark, Material accent)
    {
        var root = new GameObject(name).transform; root.SetParent(parent, false);
        Part("Baseplate", root, new Vector3(0, .015f, .015f), new Vector3(.14f, .025f, .30f), dark);
        Part("HeelCup", root, new Vector3(0, .065f, -.12f), new Vector3(.14f, .10f, .025f), accent);
        Part("HighBack", root, new Vector3(0, .13f, -.13f), new Vector3(.13f, .15f, .022f), dark);
        for (int i = 0; i < 2; i++)
        {
            Part("Strap" + i, root, new Vector3(0, .075f, -.015f + i * .11f), new Vector3(.16f, .028f, .045f), accent);
            Part("Buckle" + i, root, new Vector3(.085f, .068f, -.015f + i * .11f), new Vector3(.025f, .036f, .04f), dark);
        }
        return root;
    }

    private static GameObject Part(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name;
        part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        return part;
    }

    private static Mesh CreateDeck()
    {
        const int count = 64;
        var vertices = new Vector3[2 + count * 2];
        var top = new int[count * 3]; var bottom = new int[count * 3]; var side = new int[count * 6];
        vertices[0] = new Vector3(0, .012f, 0); vertices[1] = new Vector3(0, -.012f, 0);
        for (int i = 0; i < count; i++)
        {
            float t = i * Mathf.PI * 2 / count;
            float x = Mathf.Cos(t) * .94f;
            float z = Mathf.Sin(t) * (.145f + .035f * Mathf.Abs(Mathf.Cos(t)));
            float lift = Mathf.Pow(Mathf.Abs(x) / .94f, 6) * .075f;
            vertices[2 + i * 2] = new Vector3(x, lift + .012f, z);
            vertices[3 + i * 2] = new Vector3(x, lift - .012f, z);
            int next = (i + 1) % count;
            top[i * 3] = 0; top[i * 3 + 1] = 2 + next * 2; top[i * 3 + 2] = 2 + i * 2;
            bottom[i * 3] = 1; bottom[i * 3 + 1] = 3 + i * 2; bottom[i * 3 + 2] = 3 + next * 2;
            int a = 2 + i * 2, b = 2 + next * 2;
            side[i * 6] = a; side[i * 6 + 1] = b; side[i * 6 + 2] = a + 1;
            side[i * 6 + 3] = a + 1; side[i * 6 + 4] = b; side[i * 6 + 5] = b + 1;
        }
        var mesh = new Mesh { name = "Alpine Freeride Deck" }; mesh.vertices = vertices; mesh.subMeshCount = 3;
        mesh.SetTriangles(top, 0); mesh.SetTriangles(bottom, 1); mesh.SetTriangles(side, 2); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
