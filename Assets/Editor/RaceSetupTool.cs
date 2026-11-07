using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SkiRace;

/// <summary>
/// 离线竞速测试的编辑器工具：
/// 1) 沿主路线自动布检查点、生成起跑槽位；
/// 2) 校验赛道数据；
/// 3) 跑纯逻辑自测（不依赖网络、不依赖 Play）。
/// </summary>
public static class RaceSetupTool
{
    private const string ScenePath = "Assets/SnowboardIntegration/Snowfield_Integrated.unity";
    private const string LogFolder = "Logs";

    [MenuItem("Tools/Race/Setup Offline Race Test")]
    public static void Setup()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(LogFolder);
        File.Copy(ScenePath, Path.Combine(LogFolder, "Snowfield_Integrated.before-race-setup.unity"), true);

        GameObject route = GameObject.Find("主路线");
        if (route == null) throw new Exception("找不到主路线，无法自动布点");

        var segments = new List<Terrain>();
        foreach (Transform child in route.transform)
        {
            if (!child.name.StartsWith("Seg_")) continue;
            var terrain = child.GetComponent<Terrain>();
            if (terrain != null) segments.Add(terrain);
        }
        segments.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (segments.Count < 2) throw new Exception("主路线有效赛段不足");

        Physics.SyncTransforms();

        // 中间检查点：放在每个赛段接近末端的位置，最后一段的终点单独处理
        var centers = new List<Vector3>();
        for (int i = 0; i < segments.Count - 1; i++)
            centers.Add(RoutePoint(segments[i], 0.92f));

        Transform finishTrigger = FindTransform("FinishTrigger_终点");
        Vector3 finishPosition = finishTrigger != null
            ? finishTrigger.position
            : RoutePoint(segments[segments.Count - 1], 0.9f);

        Vector3 startBase = FindStartBase();
        if (startBase == Vector3.zero)
            startBase = centers.Count > 0 ? centers[0] - Vector3.forward * 30f : Vector3.zero;

        // 重建测试根节点
        var existing = GameObject.Find("RaceSystem_Offline");
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var system = new GameObject("RaceSystem_Offline");
        Undo.RegisterCreatedObjectUndo(system, "Create Race System");
        var track = system.AddComponent<RaceTrackDefinition>();

        var checkpointRoot = new GameObject("Checkpoints");
        checkpointRoot.transform.SetParent(system.transform, false);

        var checkpoints = new List<RaceCheckpoint>();
        for (int i = 0; i < centers.Count; i++)
        {
            Vector3 forward = ForwardAt(centers, finishPosition, i);
            float width = Mathf.Clamp(segments[i].terrainData.size.x * 0.8f, 40f, 180f);
            var checkpoint = CreateCheckpoint(
                checkpointRoot.transform,
                "CP_" + i.ToString("00") + "_" + segments[i].name,
                centers[i],
                forward,
                width,
                24f,
                i,
                false);
            checkpoints.Add(checkpoint);
        }

        Vector3 finishForward = centers.Count > 0
            ? Flatten(finishPosition - centers[centers.Count - 1])
            : Vector3.forward;
        var finish = CreateCheckpoint(
            checkpointRoot.transform,
            "CP_Finish",
            finishPosition,
            finishForward,
            60f,
            30f,
            checkpoints.Count,
            true);
        checkpoints.Add(finish);

        track.checkpoints = checkpoints.ToArray();

        // 起跑槽位：2 排 x 4 列，4 人取前排，8 人全用
        Vector3 forwardToFirst = checkpoints.Count > 0
            ? Flatten(checkpoints[0].transform.position - startBase)
            : Vector3.forward;
        if (forwardToFirst.sqrMagnitude < 0.001f) forwardToFirst = Vector3.forward;

        Vector3 right = Vector3.Cross(Vector3.up, forwardToFirst).normalized;
        var slotsRoot = new GameObject("StartSlots");
        slotsRoot.transform.SetParent(system.transform, false);

        var slots = new Transform[8];
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                int index = row * 4 + col;
                Vector3 position = startBase
                    + right * ((col - 1.5f) * 3f)
                    - forwardToFirst * (row * 4f);

                RaycastHit hit;
                if (Physics.Raycast(position + Vector3.up * 8f, Vector3.down, out hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                    position.y = hit.point.y + 0.25f;

                var slot = new GameObject("StartSlot_" + index);
                slot.transform.SetParent(slotsRoot.transform, false);
                slot.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forwardToFirst, Vector3.up));
                slots[index] = slot.transform;
            }
        }
        track.startSlots = slots;

        var director = system.AddComponent<RaceDirector>();
        director.track = track;
        director.autoStart = false;

        var simulator = system.AddComponent<LocalRaceSimulator>();
        simulator.director = director;
        simulator.track = track;
        simulator.baseGhostSpeed = 20f;
        simulator.ghostSpeedStep = 2.5f;

        var hud = system.AddComponent<RaceDebugHUD>();
        hud.director = director;
        hud.simulator = simulator;

        var resultPanel = system.AddComponent<RaceResultPanel>();
        resultPanel.director = director;

        var session = system.AddComponent<RaceSessionController>();
        session.director = director;
        session.simulator = simulator;
        session.resultPanel = resultPanel;
        resultPanel.session = session;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string report = ValidateTrack(track);
        File.WriteAllText(Path.Combine(LogFolder, "race-setup.txt"),
            "场景: " + ScenePath + "\n" +
            "检查点: " + checkpoints.Count + "\n" +
            "起跑槽位: " + slots.Length + "\n" +
            "起点: " + startBase + "\n" +
            "终点: " + finishPosition + "\n\n" + report);

        Debug.Log("[RaceSetup] 已生成 RaceSystem_Offline：检查点 " + checkpoints.Count +
                  "，起跑槽位 " + slots.Length + "。按 Play 后点击右上角“开始”即可跑 8 人离线测试。");
    }

    [MenuItem("Tools/Race/Validate Track")]
    public static void ValidateTrackMenu()
    {
        var track = UnityEngine.Object.FindObjectOfType<RaceTrackDefinition>();
        if (track == null)
        {
            Debug.LogError("[RaceSetup] 场景里没有 RaceTrackDefinition");
            return;
        }

        string report = ValidateTrack(track);
        Directory.CreateDirectory(LogFolder);
        File.WriteAllText(Path.Combine(LogFolder, "race-track-validation.txt"), report);
        Debug.Log("[RaceSetup] 校验结果：\n" + report);
    }

    public static string ValidateTrack(RaceTrackDefinition track)
    {
        var builder = new StringBuilder();
        builder.AppendLine("检查点数量: " + track.CheckpointCount);
        builder.AppendLine("起跑槽位数量: " + (track.startSlots != null ? track.startSlots.Length : 0));

        bool valid = true;
        for (int i = 0; i < track.CheckpointCount; i++)
        {
            var checkpoint = track.GetCheckpoint(i);
            if (checkpoint == null)
            {
                builder.AppendLine("  [失败] CP " + i + " 为空引用");
                valid = false;
                continue;
            }

            if (checkpoint.index != i)
            {
                builder.AppendLine("  [失败] CP " + i + " 索引为 " + checkpoint.index);
                valid = false;
            }

            var box = checkpoint.GetComponent<BoxCollider>();
            if (box == null || !box.isTrigger)
            {
                builder.AppendLine("  [失败] CP " + i + " 缺少 Trigger BoxCollider");
                valid = false;
            }

            if (i > 0)
            {
                float distance = Vector3.Distance(
                    track.GetCheckpointPosition(i - 1),
                    track.GetCheckpointPosition(i));
                builder.AppendLine("  CP " + (i - 1) + " -> CP " + i + " 距离: " + distance.ToString("F1") + "m");
                if (distance < 20f)
                {
                    builder.AppendLine("  [失败] 检查点间距过近，容易重复触发");
                    valid = false;
                }
            }
        }

        if (!track.GetCheckpoint(track.FinishIndex) || !track.GetCheckpoint(track.FinishIndex).isFinish)
        {
            builder.AppendLine("  [失败] 最后一个检查点没有标记为终点");
            valid = false;
        }

        if (track.startSlots == null || track.startSlots.Length < 4)
        {
            builder.AppendLine("  [失败] 起跑槽位少于 4 个");
            valid = false;
        }

        builder.AppendLine(valid ? "结果: PASS" : "结果: FAIL");
        return builder.ToString();
    }

    [MenuItem("Tools/Race/Validate Scene Wiring")]
    public static void ValidateSceneWiringMenu()
    {
        int result = ValidateSceneWiring();
        Debug.Log("[RaceSetup] 场景接线校验" + (result == 0 ? "通过" : "失败") + "，详见 Logs/race-wiring.txt");
    }

    /// <summary>批处理入口：Unity -executeMethod RaceSetupTool.ValidateSceneWiringBatch</summary>
    public static void ValidateSceneWiringBatch()
    {
        int result = 2;
        try
        {
            result = ValidateSceneWiring();
        }
        catch (Exception exception)
        {
            Debug.LogError("[RaceSetup] 场景接线校验异常: " + exception);
        }
        EditorApplication.Exit(result);
    }

    private static int ValidateSceneWiring()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var builder = new StringBuilder();
        bool valid = true;

        var system = GameObject.Find("RaceSystem_Offline");
        if (system == null)
        {
            builder.AppendLine("[失败] 场景里没有 RaceSystem_Offline");
            valid = false;
        }
        else
        {
            var track = system.GetComponent<RaceTrackDefinition>();
            var director = system.GetComponent<RaceDirector>();
            var simulator = system.GetComponent<LocalRaceSimulator>();
            var hud = system.GetComponent<RaceDebugHUD>();
            var resultPanel = system.GetComponent<RaceResultPanel>();
            var session = system.GetComponent<RaceSessionController>();

            if (track == null || director == null || simulator == null || hud == null
                || resultPanel == null || session == null)
            {
                builder.AppendLine("[失败] RaceSystem_Offline 组件缺失");
                valid = false;
            }
            else
            {
                if (resultPanel.director != director || resultPanel.session != session)
                {
                    builder.AppendLine("[失败] RaceResultPanel 未接线");
                    valid = false;
                }

                if (session.director != director || session.simulator != simulator || session.resultPanel != resultPanel)
                {
                    builder.AppendLine("[失败] RaceSessionController 未接线");
                    valid = false;
                }

                if (track.CheckpointCount < 2)
                {
                    builder.AppendLine("[失败] 检查点数量不足");
                    valid = false;
                }

                if (director.track != track)
                {
                    builder.AppendLine("[失败] RaceDirector.track 未接线");
                    valid = false;
                }

                if (simulator.director != director || simulator.track != track)
                {
                    builder.AppendLine("[失败] LocalRaceSimulator 未接线");
                    valid = false;
                }

                if (hud.director != director || hud.simulator != simulator)
                {
                    builder.AppendLine("[失败] RaceDebugHUD 未接线");
                    valid = false;
                }

                if (track.startSlots == null || track.startSlots.Length != 8)
                {
                    builder.AppendLine("[失败] 起跑槽位不是 8 个");
                    valid = false;
                }

                for (int i = 0; i < track.CheckpointCount; i++)
                {
                    var checkpoint = track.GetCheckpoint(i);
                    if (checkpoint == null || checkpoint.index != i)
                    {
                        builder.AppendLine("[失败] CP " + i + " 引用或索引错误");
                        valid = false;
                        continue;
                    }

                    var box = checkpoint.GetComponent<BoxCollider>();
                    if (box == null || !box.isTrigger)
                    {
                        builder.AppendLine("[失败] CP " + i + " 不是 Trigger");
                        valid = false;
                    }

                    builder.AppendLine("  CP " + i + " pos=" + checkpoint.transform.position +
                                       " finish=" + checkpoint.isFinish);
                }

                builder.AppendLine("  起跑槽位数: " + track.startSlots.Length);
                builder.AppendLine("  起点槽 0: " + track.startSlots[0].position);
            }
        }

        builder.AppendLine(valid ? "结果: PASS" : "结果: FAIL");
        Directory.CreateDirectory(LogFolder);
        File.WriteAllText(Path.Combine(LogFolder, "race-wiring.txt"), builder.ToString());
        Debug.Log("[RaceSetup] 场景接线校验:\n" + builder);
        return valid ? 0 : 1;
    }

    // ============ 自测：纯逻辑，不需要 Play ============

    [MenuItem("Tools/Race/Run Race Core Self Test")]
    public static void RunSelfTestMenu()
    {
        int result = RunSelfTest();
        Debug.Log("[RaceSetup] 自测" + (result == 0 ? "通过" : "失败") + "，详见 Logs/race-selftest.txt");
    }
    public static void RunSelfTestBatch()
    {
        int result = 2;
        try
        {
            result = RunSelfTest();
        }
        catch (Exception exception)
        {
            Debug.LogError("[RaceSetup] 自测异常: " + exception);
        }
        EditorApplication.Exit(result);
    }

    private static int RunSelfTest()
    {
        var log = new StringBuilder();
        int failures = 0;

        var root = new GameObject("RaceSelfTest");
        var track = root.AddComponent<RaceTrackDefinition>();

        var checkpointObjects = new List<GameObject>();
        var checkpoints = new RaceCheckpoint[5];
        for (int i = 0; i < checkpoints.Length; i++)
        {
            var go = new GameObject("TestCP_" + i);
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(0f, 0f, i * 100f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            var checkpoint = go.AddComponent<RaceCheckpoint>();
            checkpoint.index = i;
            checkpoint.isFinish = i == checkpoints.Length - 1;
            checkpoint.reachRadius = 2f;
            checkpoints[i] = checkpoint;
            checkpointObjects.Add(go);
        }
        track.checkpoints = checkpoints;

        var director = root.AddComponent<RaceDirector>();
        director.track = track;
        director.countdownSeconds = 2f;
        director.rankingInterval = 0.2f;
        director.raceTimeLimit = 30f;
        director.minCheckpointInterval = 0f;
        director.maxCheckpointSpeed = 10000f;
        director.resetPenaltySeconds = 3f;
        director.strictCheckpointOrder = true;

        double now = 0d;
        director.clock = () => now;

        director.RegisterRacer(1, "A");
        director.RegisterRacer(2, "B");
        director.RegisterRacer(3, "C");
        director.RegisterRacer(4, "D");

        Vector3 resetTarget = Vector3.zero;
        bool resetFired = false;
        director.RacerResetRequested += (id, position) =>
        {
            if (id != 4) return;
            resetFired = true;
            resetTarget = position;
        };

        director.StartCountdown();
        now = 2.1d;
        director.Tick(now);

        // A 最先完赛
        PassCheckpoint(director, 1, 0, ref now, 2.4d, 0);
        PassCheckpoint(director, 1, 1, ref now, 2.6d, 1);
        PassCheckpoint(director, 1, 2, ref now, 2.8d, 2);
        PassCheckpoint(director, 1, 3, ref now, 3.0d, 3);
        PassCheckpoint(director, 1, 4, ref now, 3.2d, 4);

        // B、C 同一时刻完赛，应该并列
        PassCheckpoint(director, 2, 0, ref now, 2.5d, 0);
        PassCheckpoint(director, 2, 1, ref now, 2.7d, 1);
        PassCheckpoint(director, 2, 2, ref now, 2.9d, 2);
        PassCheckpoint(director, 2, 3, ref now, 3.1d, 3);
        PassCheckpoint(director, 2, 4, ref now, 3.3d, 4);

        PassCheckpoint(director, 3, 0, ref now, 2.6d, 0);
        PassCheckpoint(director, 3, 1, ref now, 2.8d, 1);
        PassCheckpoint(director, 3, 2, ref now, 3.0d, 2);
        PassCheckpoint(director, 3, 3, ref now, 3.2d, 3);
        PassCheckpoint(director, 3, 4, ref now, 3.3d, 4);

        // D 只过第一个检查点，然后 R 复位一次
        PassCheckpoint(director, 4, 0, ref now, 2.7d, 0);
        now = 3.15d;
        director.ResetRacer(4);

        // 超时/强制结束，D 应判为未完赛
        now = 3.6d;
        director.ForceFinishRace();

        var results = director.Results;
        if (results == null || results.Count != 4)
        {
            log.AppendLine("[失败] 结算行数不为 4");
            failures++;
        }
        else
        {
            for (int i = 0; i < results.Count; i++)
            {
                var row = results[i];
                log.AppendLine("  id=" + row.racerId + " rank=" + row.rank +
                               " time=" + row.elapsedSeconds.ToString("F3") +
                               " status=" + row.status);
            }

            if (results[0].racerId != 1 || results[0].rank != 1)
            {
                log.AppendLine("[失败] 第一名不是 A");
                failures++;
            }
            if (results[1].rank != 2 || results[2].rank != 2)
            {
                log.AppendLine("[失败] B/C 没有并列第 2");
                failures++;
            }
            if (results[3].status != RacerStatus.Dnf)
            {
                log.AppendLine("[失败] D 没有判为未完赛");
                failures++;
            }
            if (Math.Abs(results[0].elapsedSeconds - 1.1d) > 0.01d)
            {
                log.AppendLine("[失败] A 完赛时间不正确: " + results[0].elapsedSeconds);
                failures++;
            }
        }

        if (!resetFired)
        {
            log.AppendLine("[失败] R 复位事件没有触发");
            failures++;
        }
        else if (Vector3.Distance(resetTarget, checkpoints[0].transform.position + Vector3.up * 1.5f) > 0.01f)
        {
            log.AppendLine("[失败] R 复位目标不是最近检查点");
            failures++;
        }

        // 再来一局：只重置状态和成绩，不重载场景
        director.StartCountdown();
        if (director.Phase != RacePhase.Countdown || director.Results.Count != 0)
        {
            log.AppendLine("[失败] 再来一局没有清空成绩/回到倒计时");
            failures++;
        }

        log.AppendLine(failures == 0 ? "结果: PASS" : "结果: FAIL (" + failures + ")");
        Debug.Log("[RaceSelfTest]\n" + log);

        Directory.CreateDirectory(LogFolder);
        File.WriteAllText(Path.Combine(LogFolder, "race-selftest.txt"), log.ToString());

        UnityEngine.Object.DestroyImmediate(root);
        return failures == 0 ? 0 : 1;
    }

    private static void PassCheckpoint(
        RaceDirector director, int racerId, int checkpointIndex, ref double now, double time, int positionIndex)
    {
        now = time;
        director.SetRacerPosition(racerId, new Vector3(0f, 0f, positionIndex * 100f), now);
        director.ReportCheckpoint(racerId, checkpointIndex, checkpointIndex == 4);
    }

    // ============ 辅助 ============

    private static RaceCheckpoint CreateCheckpoint(
        Transform parent,
        string name,
        Vector3 position,
        Vector3 forward,
        float width,
        float height,
        int index,
        bool isFinish)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward.normalized, Vector3.up));

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(width, height, 6f);
        box.center = new Vector3(0f, height * 0.5f - 2f, 0f);

        var checkpoint = go.AddComponent<RaceCheckpoint>();
        checkpoint.index = index;
        checkpoint.isFinish = isFinish;
        checkpoint.reachRadius = 2f;
        return checkpoint;
    }

    private static Vector3 RoutePoint(Terrain terrain, float t)
    {
        var data = terrain.terrainData;
        Vector3 size = data.size;
        Vector3 world = terrain.transform.TransformPoint(
            new Vector3(size.x * 0.5f, 0f, size.z * Mathf.Clamp01(t)));
        float height = terrain.SampleHeight(world);
        return new Vector3(world.x, terrain.transform.position.y + height + 0.5f, world.z);
    }

    private static Vector3 ForwardAt(List<Vector3> points, Vector3 finish, int index)
    {
        Vector3 previous = index > 0 ? points[index - 1] : points[index];
        Vector3 next = index + 1 < points.Count ? points[index + 1] : finish;
        Vector3 direction = Flatten(next - previous);
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
        return direction.normalized;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static Transform FindTransform(string name)
    {
        foreach (var transform in UnityEngine.Object.FindObjectsOfType<Transform>(true))
        {
            if (transform.name == name) return transform;
        }
        return null;
    }

    private static Vector3 FindStartBase()
    {
        foreach (var transform in UnityEngine.Object.FindObjectsOfType<Transform>(true))
        {
            if (transform.name.StartsWith("ImportedSnowboardPlayer"))
                return transform.position;
        }
        return Vector3.zero;
    }
}
