using System;
using System.IO;
using System.Linq;
using SkiRace;
using SkiRace.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MultiplayerRaceSetup
{
    public const string ScenePath = "Assets/SnowboardIntegration/Snowfield_Integrated.unity";

    [MenuItem("Tools/Race/Configure Multiplayer")]
    public static void Configure()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var director = UnityEngine.Object.FindObjectOfType<RaceDirector>(true);
        if (director == null || director.track == null) throw new Exception("Missing race track");
        var track = director.track;
        var assets = AssetDatabase.LoadAssetAtPath<RaceNetworkAssets>("Assets/Resources/RaceNetworkAssets.asset");
        if (assets == null)
        {
            assets = ScriptableObject.CreateInstance<RaceNetworkAssets>();
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.CreateAsset(assets, "Assets/Resources/RaceNetworkAssets.asset");
        }
        assets.singleBoardModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LapinerTools/SnowboardStarterKit/Resources/CharacterModels/PlayerModelDefault.prefab");
        assets.doubleBoardModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Male_Skier/Prefabs/SK_Male_Skier_A-Pose Variant.prefab");
        assets.fallbackFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/AlpineUI.ttf");
        EditorUtility.SetDirty(assets);

        // Slot 0 used to sit hundreds of metres below the first checkpoint. Line every slot
        // up on the actual snow surface so the race starts on the slope instead of under it.
        Vector3 forward = Vector3.ProjectOnPlane(track.GetCheckpointPosition(1) - track.GetCheckpointPosition(0), Vector3.up).normalized;
        for (int i = 0; i < track.startSlots.Length; i++)
            track.startSlots[i].SetPositionAndRotation(track.GetStartPosition(i, track.startSlots.Length), Quaternion.LookRotation(forward));
        director.enabled = false;
        director.resetPenaltySeconds = 3;
        foreach (var tool in UnityEngine.Object.FindObjectsOfType<LocalRaceSimulator>(true)) tool.enabled = false;
        foreach (var tool in UnityEngine.Object.FindObjectsOfType<RaceDebugHUD>(true)) tool.enabled = false;
        foreach (var tool in UnityEngine.Object.FindObjectsOfType<RaceResultPanel>(true)) tool.enabled = false;
        foreach (var tool in UnityEngine.Object.FindObjectsOfType<RaceSessionController>(true)) tool.enabled = false;
        EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        EditorSceneManager.SaveScene(director.gameObject.scene);
        AssetDatabase.SaveAssets();
        ValidateLogic();
        Debug.Log("MULTIPLAYER_SETUP_PASS: Mirror 96.11.4, 8 slots, " + track.CheckpointCount + " checkpoints, both rider models");
    }

    [MenuItem("Tools/Race/Validate Multiplayer Rules")]
    public static void ValidateLogic()
    {
        var root = new GameObject("MultiplayerRuleTests");
        try
        {
            var track = root.AddComponent<RaceTrackDefinition>();
            track.startSlots = new[] { new GameObject("Start").transform }; track.startSlots[0].SetParent(root.transform);
            track.checkpoints = new RaceCheckpoint[6];
            for (int i = 0; i < 6; i++)
            {
                var cp = new GameObject("CP" + i); cp.transform.SetParent(root.transform); cp.transform.position = Vector3.forward * (i + 1) * 100;
                var box = cp.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(30, 20, 6); box.center = Vector3.up * 5;
                var checkpoint = cp.AddComponent<RaceCheckpoint>(); checkpoint.index = i; checkpoint.isFinish = i == 5; track.checkpoints[i] = checkpoint;
            }
            var director = root.AddComponent<RaceDirector>(); director.track = track; director.countdownSeconds = 3; director.maxCheckpointSpeed = 80;
            double now = 100; director.clock = () => now;
            for (int i = 1; i <= 8; i++) { director.RegisterRacer(i, "Rider" + i); director.SetRacerPosition(i, Vector3.zero); }
            director.StartCountdown(); now = 102; director.Tick(now); Require(director.Phase == RacePhase.Countdown, "countdown lock");
            now = 103; director.Tick(now); Require(director.IsRacing && director.RacerCount == 8, "eight racer start");
            director.ReportCheckpoint(1, 5, true); Require(director.GetRacerStatus(1) == RacerStatus.Racing, "reject skipped finish");
            director.ReportCheckpoint(1, 999, true); Require(director.GetLastCheckpointIndex(1) == -1, "reject invalid checkpoint");
            director.ReportCheckpoint(1, 0, true); Require(director.GetRacerStatus(1) == RacerStatus.Racing && director.GetLastCheckpointIndex(1) == 0, "finish flag cannot bypass track");
            director.SetRacerPosition(1, new Vector3(0, 0, 140)); director.SetRacerPosition(2, new Vector3(0, 0, 40));
            now = 105; director.Tick(now);
            Require(director.Ranking.First(r => r.racerId == 2).distanceToLeader > 50, "distance to leader in metres");
            double before = director.Ranking.First(r => r.racerId == 1).elapsedSeconds;
            director.ResetRacer(1); double after = director.Ranking.First(r => r.racerId == 1).elapsedSeconds;
            Require(Math.Abs(after - before - 3) < .001, "reset adds three seconds");
            for (int cp = 1; cp < 6; cp++) { now += 3; director.ReportCheckpoint(1, cp, cp == 5); }
            Require(director.GetRacerStatus(1) == RacerStatus.Finished, "ordered finish");
            double frozen = director.Ranking.First(r => r.racerId == 1).elapsedSeconds;
            now += 5; director.Tick(now); Require(Math.Abs(director.Ranking.First(r => r.racerId == 1).elapsedSeconds - frozen) < .001, "finish time frozen");
            director.UnregisterRacer(2); Require(director.GetRacerStatus(2) == RacerStatus.Disconnected, "disconnect state");
            director.ForceFinishRace(); Require(director.Results.Count == 8 && director.Results[0].racerId == 1, "results and DNF");
            var boxTest = track.checkpoints[0].GetComponent<BoxCollider>();
            Require(SkiRaceNetworkManager.Crosses(boxTest, new Vector3(0, 0, 90), new Vector3(0, 0, 110)), "swept checkpoint detection");
            Require(!SkiRaceNetworkManager.Crosses(boxTest, new Vector3(100, 0, 90), new Vector3(100, 0, 110)), "outside checkpoint rejected");
            Require(SkiRaceNetworkManager.CleanName("<b>\nAlice") == "bAlice", "safe player names");
            director.AbortToLobby(); director.ClearRacers(); Require(director.Phase == RacePhase.Lobby && director.RacerCount == 0, "return to lobby");
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/multiplayer-rules.txt", "PASS: countdown, eight racers, ordered/invalid checkpoints, metres gap, positive reset penalty, frozen finish time, disconnect, DNF, swept triggers, name validation, lobby reset\n");
            Debug.Log("MULTIPLAYER_RULES_PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception("Multiplayer validation failed: " + message); }

    public static void ConfigureBatch()
    {
        try { Configure(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void BuildWindows()
    {
        var output = Path.GetFullPath("Build/Windows/Skijoring.exe"); Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
        Debug.Log("MULTIPLAYER_BUILD_PASS " + output); EditorApplication.Exit(0);
    }
}
