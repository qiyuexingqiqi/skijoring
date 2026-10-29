using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

public static class SkiLightThemeBuilder
{
    [MenuItem("Tools/Skiing/Rebuild All UI with Ice Blue Theme")]
    public static void Build()
    {
        // Rebind weather/HUD after the race system is rebuilt, preserving all scene references.
        EquipmentSelectionBuilder.Build();
        SkiRaceSceneBuilder.Build();
        SkiWeatherSceneBuilder.Build();
    }

    public static void BuildAndValidate()
    {
        Build();
        SkiWeatherValidation.Run();
    }

    [System.Serializable]
    private sealed class Dependencies { public string[] paths; }

    public static void WriteDeliveryDependencies()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        var weather = Object.FindObjectOfType<SkiWeatherController>(true);
        var materials = weather.rainEffect.GetComponentsInChildren<Renderer>(true)
            .Concat(weather.snowEffect.GetComponentsInChildren<Renderer>(true))
            .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null)
            .Select(AssetDatabase.GetAssetPath);
        string[] seeds = materials.Concat(new[] { "Assets/下雪花特效/Snowfall.prefab" }).Distinct().ToArray();
        var dependencies = new Dependencies { paths = AssetDatabase.GetDependencies(seeds, true)
            .Where(path => path.StartsWith("Assets/")).OrderBy(path => path).ToArray() };
        Directory.CreateDirectory("Logs/Packaging");
        File.WriteAllText("Logs/Packaging/weather-dependencies.json", JsonUtility.ToJson(dependencies, true));
    }
}
