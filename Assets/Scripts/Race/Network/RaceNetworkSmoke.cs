using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using Mirror;
#endif

namespace SkiRace.Network
{
    // Only development builds accept the explicit automation arguments.
    public sealed class RaceNetworkSmoke : MonoBehaviour
    {
        public static void InstallIfRequested(SkiRaceNetworkManager manager)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-raceSmoke") >= 0)
                manager.gameObject.AddComponent<RaceNetworkSmoke>();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private SkiRaceNetworkManager manager;
        private string role, report, roomFile;
        private int expected;
        private double started, nextAction, racingAt;
        private bool created, resetRequested, resetObserved, finishedObserved, returned, rematchStarted;
        private int maxRemotes, maxRows;
        private double frozenTime = -1;
        private int waypoint;
        private int screenshots;
        private double resetDeadline;
        private double nextDiag;
        private static int trainingStage;
        private bool menuShown, lobbyCaptured;

        private string Arg(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private void Start()
        {
            manager = GetComponent<SkiRaceNetworkManager>(); role = Arg("-raceSmoke", "client");
            report = Arg("-raceReport", Path.Combine(Application.persistentDataPath, "network-smoke.json"));
            roomFile = Arg("-raceRoom", Path.Combine(Application.temporaryCachePath, "race-room.txt"));
            expected = int.Parse(Arg("-racePlayers", "4")); started = Time.realtimeSinceStartupAsDouble;
            manager.PlayerName = Arg("-raceName", role); manager.DoubleBoard = Arg("-raceEquipment", "single") == "double";
            Application.targetFrameRate = 30; QualitySettings.vSyncCount = 0;
            if (role == "host")
            {
                var boards = FindObjectsOfType<CarvingController>(true).Select(c => (c.name + ":" + c.gameObject.activeInHierarchy))
                    .Concat(FindObjectsOfType<Snowboard.PlayerController>(true).Select(c => (c.name + ":" + c.gameObject.activeInHierarchy)));
                Debug.Log("DIAG_BOARDS " + string.Join(",", boards.ToArray()));
            }
        }

        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - started > 170) { Finish(false, "timeout: " + manager.Status + " phase=" + manager.Race.phase); return; }
            if (now - started < 1) return;
            if (screenshots == 0) { Capture("mode"); screenshots++; return; }
            if (now - started < 3) return;
            if (role == "training") { TickTraining(now); return; }
            if (role == "lobbyhost" || role == "disconnecthost" || role == "idle" || role == "wrongcode" || role == "full" || role == "disconnect")
            { TickBoundaries(now); return; }
            if (!created)
            {
                manager.Multiplayer();
                if (role == "host")
                {
                    manager.Capacity = expected; manager.CreateRoom(); created = true;
                }
                else if (File.Exists(roomFile))
                {
                    string code = File.ReadAllText(roomFile).Trim();
                    // At least one client uses the UDP room-code lookup; others exercise TCP direct join.
                    manager.JoinRoom(code, Arg("-raceDiscovery", "0") == "1" ? "" : "127.0.0.1"); created = true;
                }
                return;
            }
            if (!manager.InRoom) return;
            if (role == "host" && !File.Exists(roomFile)) File.WriteAllText(roomFile, manager.Room.code);
            maxRemotes = Math.Max(maxRemotes, manager.RemoteRiders.Count);
            maxRows = Math.Max(maxRows, manager.Race.rows != null ? manager.Race.rows.Length : 0);
            if (manager.Race.phase == RacePhase.Lobby && now >= nextAction)
            {
                nextAction = now + 1;
                if (manager.Round > 0) returned = true;
                var self = manager.Room.members.First(m => m.id == manager.LocalId);
                if (!self.ready) manager.ToggleReady();
                else if (role == "host" && manager.Room.members.Length == expected && manager.Room.members.All(m => m.ready))
                    manager.StartRound();
            }
            if (manager.Round >= 2 && manager.Race.phase == RacePhase.Countdown)
            {
                rematchStarted = true;
                Finish(resetObserved && finishedObserved && maxRows == expected && maxRemotes == expected - 1 && returned, "round, reset, finish, lobby and rematch");
                return;
            }
            if (manager.LocalRider == null) return;
            manager.LocalRider.SetLocked(true);
            if (manager.Race.phase == RacePhase.Racing)
            {
                if (racingAt == 0) { racingAt = now; Capture("racing"); }
                var self = manager.Race.rows.FirstOrDefault(r => r.racerId == manager.LocalId);
                if (self.status == RacerStatus.Finished)
                {
                    finishedObserved = true;
                    if (frozenTime < 0) frozenTime = self.elapsedSeconds;
                    else if (Math.Abs(frozenTime - self.elapsedSeconds) > .001) { Finish(false, "finish time changed"); return; }
                    return;
                }
                if (role == "host" && now >= nextDiag)
                {
                    nextDiag = now + 1;
                    var cam = Camera.main;
                    var vcam = FindObjectOfType<Cinemachine.CinemachineVirtualCamera>();
                    Debug.Log("DIAG t=" + (now - started).ToString("F1") + " phase=" + manager.Race.phase + " wp=" + waypoint
                        + " cpi=" + self.checkpointIndex + " status=" + self.status + " rev=" + manager.LocalRevision
                        + " pos=" + manager.LocalRider.transform.position.ToString("F1")
                        + " cam=" + (cam != null ? cam.transform.position.ToString("F1") : "none")
                        + " follow=" + (vcam != null && vcam.Follow != null ? vcam.Follow.name + "@" + vcam.Follow.position.ToString("F1") : "none"));
                }
                if (self.checkpointIndex >= 0 && !resetRequested)
                { manager.RequestReset(); resetRequested = true; resetDeadline = now + 2; nextAction = now + .5; return; }
                if (resetRequested && manager.LocalRevision >= 2) resetObserved = true;
                if (resetRequested && !resetObserved && now < resetDeadline) return;
                waypoint = Mathf.Max(waypoint, self.checkpointIndex + 1);
                if (waypoint >= manager.director.track.CheckpointCount) return;
                Vector3 target = manager.director.track.GetCheckpointPosition(waypoint) + Vector3.up * 1.5f;
                Vector3 pos = manager.LocalRider.transform.position;
                Vector3 delta = target - pos;
                Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
                Quaternion rotation = flat.sqrMagnitude > .01f ? Quaternion.LookRotation(flat) : manager.LocalRider.transform.rotation;
                int slot = manager.Room.members.First(m => m.id == manager.LocalId).slot;
                manager.LocalRider.Teleport(Vector3.MoveTowards(pos, target, (58 - slot * 1.5f) * Time.unscaledDeltaTime), rotation);
                // Wait for the authoritative checkpoint acknowledgement before advancing.
            }
            if (manager.Race.phase == RacePhase.Finished && now >= nextAction)
            {
                finishedObserved |= manager.Race.rows.Any(r => r.racerId == manager.LocalId && r.status == RacerStatus.Finished);
                if (screenshots == 1) { Capture("results"); screenshots++; nextAction = now + 1; return; }
                if (role == "host") { nextAction = now + 2; manager.ReturnToRoom(); }
            }
        }

        private void Capture(string suffix)
        {
            if (Arg("-raceCapture", "0") != "1") return;
            StartCoroutine(CaptureLayout(suffix));
        }

        private System.Collections.IEnumerator CaptureLayout(string suffix)
        {
            yield return null;
            var camera = Camera.main;
            if (camera == null) camera = FindObjectsOfType<Camera>(true).FirstOrDefault();
            if (camera == null) yield break;
            var canvases = FindObjectsOfType<Canvas>().Where(c => c.enabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var target = new RenderTexture(1280, 800, 24);
            var uiObject = new GameObject("CaptureUICamera");
            var uiCamera = uiObject.AddComponent<Camera>(); uiCamera.enabled = false;
            uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.cullingMask = ~0;
            uiCamera.transform.position = new Vector3(10000, 10000, -10000);
            uiCamera.nearClipPlane = .01f; uiCamera.farClipPlane = 100;
            uiCamera.orthographic = true; uiCamera.orthographicSize = 400;
            uiCamera.targetTexture = target;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1; }
            camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); camera.Render(); uiCamera.Render(); RenderTexture.active = target;
            var pixels = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); pixels.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));
            File.WriteAllBytes(Path.ChangeExtension(report, null) + "-" + suffix + ".png", pixels.EncodeToPNG());
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
            Destroy(pixels); target.Release(); Destroy(target); Destroy(uiObject);
        }

        [Serializable] private sealed class Result
        {
            public bool passed, resetObserved, finishedObserved, returned, rematchStarted;
            public int participants, maxRemotes, maxRows, receivedPoses, receivedSnapshots;
            public string role, detail;
            public double finishedTime;
        }

        private void Finish(bool passed, string detail)
        {
            enabled = false;
            var result = new Result { passed = passed, role = role, participants = expected, maxRemotes = maxRemotes, maxRows = maxRows,
                resetObserved = resetObserved, finishedObserved = finishedObserved, returned = returned, rematchStarted = rematchStarted,
                receivedPoses = manager.ReceivedPoses, receivedSnapshots = manager.ReceivedSnapshots, detail = detail, finishedTime = frozenTime };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));
            File.WriteAllText(report, JsonUtility.ToJson(result, true));
            Debug.Log("NETWORK_SMOKE_" + (passed ? "PASS " : "FAIL ") + detail);
            // Host waits so every client receives the rematch snapshot before shutdown.
            if (role == "host") Invoke(nameof(QuitSuccess), 3); else Application.Quit(passed ? 0 : 1);
        }

        private void QuitSuccess() { Application.Quit(); }

        private void TickBoundaries(double now)
        {
            bool host = role == "lobbyhost" || role == "disconnecthost";
            if (!menuShown) { manager.Multiplayer(); Capture("connect"); menuShown = true; return; }
            if (now - started < 5) return;
            if (!created)
            {
                if (host) { manager.Capacity = expected; manager.CreateRoom(); created = true; }
                else if (File.Exists(roomFile))
                {
                    if (role == "full" && (!File.Exists(roomFile + ".count") || File.ReadAllText(roomFile + ".count") != expected.ToString())) return;
                    string code = File.ReadAllText(roomFile).Trim();
                    if (role == "wrongcode") code = code == "999999" ? "999998" : "999999";
                    manager.JoinRoom(code, "127.0.0.1"); created = true;
                }
                return;
            }
            if (role == "wrongcode" || role == "full")
            {
                if (!NetworkClient.active && !manager.IsConnecting)
                    Finish(manager.Status.Contains(role == "full" ? "已满" : "不正确"), manager.Status);
                return;
            }
            if (manager.InRoom)
            {
                finishedObserved = true;
                if (host)
                {
                    File.WriteAllText(roomFile, manager.Room.code);
                    File.WriteAllText(roomFile + ".count", manager.Room.members.Length.ToString());
                    if (manager.Room.members.Length == expected && !lobbyCaptured) { Capture("lobby"); lobbyCaptured = true; }
                }
                if ((role == "disconnecthost" || role == "disconnect") && now >= nextAction)
                {
                    nextAction = now + 1;
                    var self = manager.Room.members.First(m => m.id == manager.LocalId);
                    if (manager.Race.phase == RacePhase.Lobby && !self.ready) manager.ToggleReady();
                    else if (role == "disconnecthost" && manager.Race.phase == RacePhase.Lobby && manager.Room.members.Length == expected && manager.Room.members.All(m => m.ready)) manager.StartRound();
                }
                if (role == "disconnecthost" && manager.Race.phase == RacePhase.Racing)
                {
                    if (racingAt == 0) racingAt = now;
                    if (now - racingAt > 5) { manager.Leave(); Finish(true, "host shutdown during race"); }
                }
                if (role == "lobbyhost" && now - started > 20) { manager.Leave(); Finish(lobbyCaptured, "full room lobby and host shutdown"); }
            }
            else if (finishedObserved && !NetworkClient.active)
                Finish(manager.RemoteRiders.Count == 0 && manager.LocalRider == null && manager.ui.Screen == RaceNetworkUI.Page.Connect, "disconnect returned to room connection page");
        }

        private void TickTraining(double now)
        {
            double age = now - started;
            var clock = manager.trainingClock;
            if (trainingStage == 0)
            {
                if (!created) { manager.Train(); Capture("equipment"); created = true; return; }
                if (age > 4 && !clock.IsRunning && !clock.IsFinished) manager.gear.OnClickSingle();
                if (age > 7 && !finishedObserved)
                {
                    if (!clock.IsRunning || clock.ElapsedSeconds < 1 || manager.ui.Screen != RaceNetworkUI.Page.Training)
                    { Finish(false, "single training clock/menu"); return; }
                    Capture("single-timer");
                    if (!clock.TryFinish(manager.gear.boardSingle.GetComponent<Collider>())) { Finish(false, "single finish"); return; }
                    finishedObserved = true;
                }
                if (age > 8 && screenshots == 1) { Capture("single-finish"); screenshots++; }
                if (age > 10) { trainingStage = 1; clock.Retry(); }
            }
            else if (trainingStage == 1)
            {
                if (!clock.IsRunning || !manager.gear.boardSingle.activeInHierarchy || manager.ui.Screen != RaceNetworkUI.Page.Training)
                { Finish(false, "retry did not preserve training mode"); return; }
                Capture("retry"); trainingStage = 2; clock.ReturnToSelection();
            }
            else
            {
                if (!created)
                {
                    if (!manager.gear.selectUIPanel.activeInHierarchy || clock.IsRunning) { Finish(false, "return to equipment selection"); return; }
                    Capture("returned-selection"); created = true; return;
                }
                if (age > 5 && !clock.IsRunning && !clock.IsFinished) manager.gear.OnClickDouble();
                if (age > 8 && !finishedObserved)
                {
                    if (!clock.IsRunning || !manager.gear.boardDouble.activeInHierarchy || manager.gear.boardDouble.GetComponentInChildren<SkierTurnBalance>() == null)
                    { Finish(false, "double training and upper body component"); return; }
                    Capture("double-timer");
                    if (!clock.TryFinish(manager.gear.boardDouble.GetComponent<Collider>())) { Finish(false, "double finish"); return; }
                    finishedObserved = true;
                }
                if (age > 9 && screenshots == 1) { Capture("double-finish"); screenshots++; }
                if (age > 11) Finish(true, "single/double training, timer, finish, retry and selection return");
            }
        }
#endif
    }
}
