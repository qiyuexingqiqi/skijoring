using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkiRace.Network
{
    [DefaultExecutionOrder(-450)]
    public sealed class SkiRaceNetworkManager : NetworkManager
    {
        public const int Protocol = 1;
        public const int DefaultPort = 7777;
        public static SkiRaceNetworkManager Instance => singleton as SkiRaceNetworkManager;
        public RaceNetworkAssets assets;
        public RaceDirector director;
        public GearSelect gear;
        public SkiRaceManager trainingClock;
        public RaceRoomDiscovery discovery;
        public RaceNetworkUI ui;
        public bool MultiplayerSelected { get; private set; }
        public bool IsConnecting { get; private set; }
        public bool InRoom => LocalId > 0 && NetworkClient.isConnected;
        public int LocalId { get; private set; }
        public int LocalRevision { get; private set; }
        public int Round { get; private set; }
        public RoomSnapshot Room { get; private set; }
        public RaceSnapshot Race { get; private set; }
        public string Status { get; private set; } = "";
        public bool IsRoomHost => InRoom && Room.hostId == LocalId;
        public RaceLocalRider LocalRider { get; private set; }
        public IReadOnlyDictionary<int, RaceRiderView> RemoteRiders => views;
        public string PlayerName { get; set; } = "滑雪玩家";
        public bool DoubleBoard { get; set; }
        public int Capacity { get; set; } = 8;
        public string PendingCode { get; private set; } = "";
        public double SnapshotReceivedAt { get; private set; }
        public int ReceivedPoses { get; private set; }
        public int ReceivedSnapshots { get; private set; }

        private sealed class Peer
        {
            public NetworkConnectionToClient connection;
            public RoomMember member;
            public Vector3 position;
            public bool hasPose;
            public double lastPoseTime;
            public double nextReset;
            public double nextAction;
            public int revision;
            public uint sequence;
        }

        private readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        private readonly Dictionary<int, double> pending = new Dictionary<int, double>();
        private readonly Dictionary<int, RaceRiderView> views = new Dictionary<int, RaceRiderView>();
        private string serverCode;
        private int hostId;
        private double nextBroadcast;
        private double connectionDeadline;
        private bool leaving;
        private static bool trainingSession;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureInstalled();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { EnsureInstalled(); }

        private static void EnsureInstalled()
        {
            if (Instance != null || FindObjectOfType<GearSelect>(true) == null) return;
            var go = new GameObject("MultiplayerRace");
            go.SetActive(false);
            var kcp = go.AddComponent<kcp2k.KcpTransport>();
            kcp.port = (ushort)DefaultPort;
            var manager = go.AddComponent<SkiRaceNetworkManager>();
            manager.transport = kcp;
            manager.dontDestroyOnLoad = false;
            manager.autoCreatePlayer = false;
            manager.maxConnections = 8;
            manager.sendRate = 30;
            manager.assets = Resources.Load<RaceNetworkAssets>("RaceNetworkAssets");
            manager.discovery = go.AddComponent<RaceRoomDiscovery>();
            manager.ui = go.AddComponent<RaceNetworkUI>();
            go.SetActive(true);
        }

        public override void Awake()
        {
            base.Awake();
            Application.runInBackground = true;
            director = FindObjectOfType<RaceDirector>(true);
            gear = FindObjectOfType<GearSelect>(true);
            trainingClock = FindObjectOfType<SkiRaceManager>(true);
            // Retain the old test tools in the project, but keep them out of the player flow.
            foreach (var tool in FindObjectsOfType<LocalRaceSimulator>(true)) tool.enabled = false;
            foreach (var tool in FindObjectsOfType<RaceDebugHUD>(true)) tool.enabled = false;
            foreach (var tool in FindObjectsOfType<RaceResultPanel>(true)) { tool.Hide(); tool.enabled = false; }
            foreach (var tool in FindObjectsOfType<RaceSessionController>(true)) tool.enabled = false;
            if (director != null)
            {
                director.enabled = false;
                director.clock = () => Time.unscaledTimeAsDouble;
                director.RacerResetRequested += ServerReset;
                director.RacerFinished += ServerFinished;
            }
        }

        public override void Start()
        {
            // NetworkManager's dedicated-server auto-start is intentionally replaced by the mode menu.
            if (trainingSession) Train(); else Home();
            RaceNetworkSmoke.InstallIfRequested(this);
        }

        public void SetStatus(string status) { Status = status; }

        public void Home()
        {
            discovery.Stop(); IsConnecting = false;
            if (NetworkClient.active || NetworkServer.active) Leave();
            MultiplayerSelected = false;
            trainingSession = false;
            HidePlayers();
            ui.Screen = RaceNetworkUI.Page.Mode;
        }

        public void Train()
        {
            MultiplayerSelected = false;
            trainingSession = true;
            ui.Screen = RaceNetworkUI.Page.Training;
            gear.selectUIPanel.SetActive(true);
        }

        public void Multiplayer()
        {
            MultiplayerSelected = true;
            trainingSession = false;
            HidePlayers();
            Status = "同一局域网内，创建房间后分享六位房间号。";
            ui.Screen = RaceNetworkUI.Page.Connect;
        }

        private void HidePlayers()
        {
            if (gear == null) return;
            gear.boardSingle.SetActive(false);
            gear.boardDouble.SetActive(false);
            gear.selectUIPanel.SetActive(false);
        }

        public void CreateRoom()
        {
            if (IsConnecting || NetworkClient.active || NetworkServer.active) return;
            if (!ValidateConfiguration()) return;
            // Hosting always listens on the canonical port, whatever a previous direct join used.
            if (transport is kcp2k.KcpTransport kcp) kcp.Port = (ushort)DefaultPort;
            MultiplayerSelected = true;
            Capacity = Mathf.Clamp(Capacity, 2, 8);
            // Handshakes do not occupy gameplay slots; allow concurrent rejected joins to receive a reason.
            maxConnections = Capacity + 4;
            serverCode = UnityEngine.Random.Range(100000, 1000000).ToString();
            PendingCode = serverCode;
            Status = "正在创建房间…";
            leaving = false;
            IsConnecting = true;
            try { StartHost(); }
            catch (Exception e) { Status = "创建失败：" + e.Message; Leave(false); }
        }

        public void JoinRoom(string code, string address = "")
        {
            if (IsConnecting || NetworkClient.active || NetworkServer.active) return;
            code = code.Trim();
            if (code.Length != 6 || code.Any(c => c < '0' || c > '9')) { Status = "请输入六位数字房间号。"; return; }
            if (!ValidateConfiguration()) return;
            MultiplayerSelected = true;
            PendingCode = code;
            leaving = false;
            IsConnecting = true;
            Status = "正在搜索房间…";
            if (!string.IsNullOrWhiteSpace(address)) Connect(address.Trim());
            else discovery.Search(code, Connect, message => { IsConnecting = false; SetStatus(message); });
        }

        private bool ValidateConfiguration()
        {
            if (assets != null && assets.singleBoardModel != null && assets.doubleBoardModel != null
                && director != null && director.track != null && director.track.CheckpointCount >= 5) return true;
            Status = "竞速资源未配置，请运行 Tools/Race/Configure Multiplayer。";
            return false;
        }

        private void Connect(string address)
        {
            // Direct joins also accept "host:port" so tunnels may expose any UDP port.
            string host = address.Trim();
            int port = DefaultPort;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && colon < host.Length - 1 && !host.Contains("]"))
            {
                string tail = host.Substring(colon + 1);
                if (int.TryParse(tail, out int parsed) && parsed > 0 && parsed < 65536)
                {
                    host = host.Substring(0, colon).Trim();
                    port = parsed;
                }
            }
            if (transport is kcp2k.KcpTransport kcp) kcp.Port = (ushort)port;
            networkAddress = host;
            connectionDeadline = Time.realtimeSinceStartupAsDouble + 12;
            Status = "正在连接房间…";
            StartClient();
        }

        public override void OnStartServer()
        {
            peers.Clear(); pending.Clear(); hostId = 0; Round = 0;
            director.AbortToLobby(); director.ClearRacers(); director.enabled = true;
            NetworkServer.RegisterHandler<JoinRaceRoom>(ServerJoin);
            NetworkServer.RegisterHandler<RoomAction>(ServerAction);
            NetworkServer.RegisterHandler<RiderPose>(ServerPose);
            discovery.Advertise(serverCode);
        }

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<RoomRejected>(msg => { Status = msg.reason; });
            NetworkClient.RegisterHandler<RoomSnapshot>(ClientRoom);
            NetworkClient.RegisterHandler<RaceSnapshot>(ClientRace);
            NetworkClient.RegisterHandler<RiderPose>(ClientPose);
            NetworkClient.RegisterHandler<ResetRider>(ClientReset);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            NetworkClient.Send(new JoinRaceRoom { protocol = Protocol, roomCode = PendingCode, displayName = PlayerName, doubleBoard = DoubleBoard });
        }

        public override void OnServerConnect(NetworkConnectionToClient conn) { pending[conn.connectionId] = Time.realtimeSinceStartupAsDouble + 10; }

        private void ServerJoin(NetworkConnectionToClient conn, JoinRaceRoom msg)
        {
            if (peers.ContainsKey(conn.connectionId)) return;
            string error = msg.protocol != Protocol ? "客户端版本不一致。"
                : msg.roomCode != serverCode ? "房间号不正确。"
                : director.Phase != RacePhase.Lobby ? "比赛已开始，请等待下一局。"
                : peers.Count >= Capacity ? "房间已满。" : null;
            if (error != null) { conn.Send(new RoomRejected { reason = error }); StartCoroutine(Reject(conn)); return; }
            string clean = CleanName(msg.displayName);
            int slot = Enumerable.Range(0, Capacity).First(i => peers.Values.All(p => p.member.slot != i));
            var peer = new Peer
            {
                connection = conn,
                member = new RoomMember { id = conn.connectionId + 1, name = clean, doubleBoard = msg.doubleBoard, slot = slot }
            };
            peers.Add(conn.connectionId, peer);
            pending.Remove(conn.connectionId);
            if (hostId == 0) hostId = peer.member.id;
            BroadcastRoom();
        }

        public static string CleanName(string name)
        {
            string value = new string((name ?? "").Where(c => !char.IsControl(c) && c != '<' && c != '>' && c != '|').Take(16).ToArray()).Trim();
            return value.Length > 0 ? value : "滑雪玩家";
        }

        private IEnumerator Reject(NetworkConnectionToClient conn) { yield return new WaitForSecondsRealtime(.3f); conn.Disconnect(); }

        public void ToggleReady() { if (InRoom) NetworkClient.Send(new RoomAction { action = 0 }); }
        public void StartRound() { if (InRoom) NetworkClient.Send(new RoomAction { action = 1 }); }
        public void RequestReset() { if (InRoom) NetworkClient.Send(new RoomAction { action = 2 }); }
        public void ReturnToRoom() { if (InRoom) NetworkClient.Send(new RoomAction { action = 3 }); }

        private void ServerAction(NetworkConnectionToClient conn, RoomAction msg)
        {
            if (!peers.TryGetValue(conn.connectionId, out var peer)) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < peer.nextAction) return;
            peer.nextAction = now + .15;
            if (msg.action == 0 && director.Phase == RacePhase.Lobby)
            {
                peer.member.ready = !peer.member.ready; BroadcastRoom();
            }
            else if (msg.action == 1 && peer.member.id == hostId && director.Phase == RacePhase.Lobby
                && peers.Count >= 2 && peers.Values.All(p => p.member.ready)) BeginRound();
            else if (msg.action == 2 && director.Phase == RacePhase.Racing && now >= peer.nextReset)
            {
                peer.nextReset = now + 1; director.ResetRacer(peer.member.id);
            }
            else if (msg.action == 3 && director.Phase == RacePhase.Finished)
            {
                director.AbortToLobby(); director.ClearRacers();
                foreach (var p in peers.Values) p.member.ready = false;
                BroadcastRoom(); BroadcastRace();
            }
        }

        private void BeginRound()
        {
            Round++;
            director.ClearRacers();
            int count = peers.Count;
            foreach (var peer in peers.Values)
            {
                var slot = director.track.GetStartSlot(peer.member.slot);
                peer.position = director.track.GetStartPosition(peer.member.slot, count) + Vector3.up * .5f;
                peer.hasPose = false; peer.sequence = 0; peer.revision++;
                director.RegisterRacer(peer.member.id, peer.member.name);
                director.SetRacerPosition(peer.member.id, peer.position);
                peer.connection.Send(new ResetRider { round = Round, revision = peer.revision, position = peer.position, rotation = slot.rotation });
            }
            director.StartCountdown();
            discovery.Stop();
            BroadcastRace();
        }

        private void BroadcastRoom()
        {
            RoomMember[] members = peers.Values.Select(p => p.member).OrderBy(m => m.slot).ToArray();
            foreach (var peer in peers.Values)
                peer.connection.Send(new RoomSnapshot { code = serverCode, capacity = Capacity, hostId = hostId, yourId = peer.member.id, members = members });
        }

        private void BroadcastRace()
        {
            var rows = (director.Phase == RacePhase.Finished ? director.Results : director.Ranking).ToArray();
            var message = new RaceSnapshot { round = Round, phase = director.Phase, time = director.CurrentRaceTime, countdown = director.CountdownRemaining, rows = rows };
            foreach (var peer in peers.Values) peer.connection.Send(message);
        }

        private void ClientRoom(RoomSnapshot message)
        {
            Room = message; LocalId = message.yourId; PendingCode = message.code;
            IsConnecting = false;
            Status = "房间已连接，选择准备后等待房主开始。";
            if (Race.phase == RacePhase.Lobby) ui.Screen = RaceNetworkUI.Page.Lobby;
            foreach (int id in views.Keys.Where(id => message.members.All(m => m.id != id)).ToArray()) { Destroy(views[id].gameObject); views.Remove(id); }
        }

        private void ClientRace(RaceSnapshot message)
        {
            ReceivedSnapshots++;
            Race = message; Round = message.round; SnapshotReceivedAt = Time.realtimeSinceStartupAsDouble;
            if (message.phase == RacePhase.Lobby)
            {
                ClearRiders(); HidePlayers(); trainingClock.EndNetworkRun(); ui.Screen = RaceNetworkUI.Page.Lobby;
                if (NetworkServer.active) discovery.Advertise(serverCode);
                return;
            }
            PrepareRiders();
            ui.Screen = message.phase == RacePhase.Finished ? RaceNetworkUI.Page.Results : RaceNetworkUI.Page.Racing;
        }

        private void PrepareRiders()
        {
            if (Room.members == null) return;
            if (LocalRider == null)
            {
                var member = Room.members.First(m => m.id == LocalId);
                var player = member.doubleBoard ? gear.boardDouble : gear.boardSingle;
                player.SetActive(true); gear.selectUIPanel.SetActive(false);
                LocalRider = player.AddComponent<RaceLocalRider>(); LocalRider.owner = this;
                trainingClock.BeginNetworkRun(player, member.doubleBoard);
            }
            foreach (var member in Room.members)
            {
                if (member.id == LocalId || views.ContainsKey(member.id)) continue;
                var go = new GameObject("NetworkRider_" + member.id);
                var view = go.AddComponent<RaceRiderView>(); view.Initialize(member, assets); views.Add(member.id, view);
                var slot = director.track.GetStartSlot(member.slot);
                go.transform.SetPositionAndRotation(director.track.GetStartPosition(member.slot, Room.members.Length) + Vector3.up * .5f, slot.rotation);
            }
        }

        private void ClientReset(ResetRider message)
        {
            if (message.round < Round || (message.round == Round && message.revision < LocalRevision)) return;
            Round = message.round; LocalRevision = message.revision;
            PrepareRiders();
            LocalRider.SetLocked(true);
            LocalRider.Teleport(message.position, message.rotation);
        }

        public void SendPose(RiderPose pose) { if (InRoom && Race.phase != RacePhase.Lobby) NetworkClient.Send(pose); }

        private void ClientPose(RiderPose pose)
        {
            if (pose.id != LocalId && pose.round == Round && views.TryGetValue(pose.id, out var view)) { ReceivedPoses++; view.Receive(pose); }
        }

        private void ServerPose(NetworkConnectionToClient conn, RiderPose pose)
        {
            if (!peers.TryGetValue(conn.connectionId, out var peer) || pose.round != Round || pose.revision != peer.revision
                || pose.sequence <= peer.sequence || director.Phase == RacePhase.Lobby) return;
            if (!Finite(pose.position) || !Finite(pose.velocity) || !Finite(pose.visualOffset) || pose.visualOffset.sqrMagnitude > 100
                || !Finite(pose.rotation) || !Finite(pose.visualRotation) || !Finite(pose.turn) || !Finite(pose.animationTime)) return;
            if (director.GetRacerStatus(peer.member.id) == RacerStatus.Finished) return;
            double now = Time.realtimeSinceStartupAsDouble;
            double sincePose = peer.hasPose ? Math.Max(0, now - peer.lastPoseTime) : 0;
            float allowance = peer.hasPose ? (float)(Math.Max(80, director.maxCheckpointSpeed) * Math.Max(.01, sincePose) + 5) : 12;
            // A network stall must never freeze a rider forever: after 0.6 s accept the reported spot.
            if (peer.hasPose && sincePose > .6) allowance = Mathf.Max(allowance, 1000f);
            if (Vector3.Distance(peer.position, pose.position) > allowance || pose.velocity.magnitude > 160) return;
            Vector3 previous = peer.position;
            peer.position = pose.position; peer.lastPoseTime = now; peer.hasPose = true; peer.sequence = pose.sequence;
            director.SetRacerPosition(peer.member.id, peer.position);
            if (director.Phase == RacePhase.Racing)
            {
                int next = director.GetLastCheckpointIndex(peer.member.id) + 1;
                var checkpoint = director.track.GetCheckpoint(next);
                if (checkpoint != null && Crosses(checkpoint.GetComponent<BoxCollider>(), previous, peer.position))
                    director.ReportCheckpoint(peer.member.id, next, next == director.track.FinishIndex);
            }
            pose.id = peer.member.id;
            pose.turn = Mathf.Clamp(pose.turn, -1, 1);
            foreach (var recipient in peers.Values) if (recipient != peer) recipient.connection.Send(pose);
        }

        public static bool Crosses(BoxCollider box, Vector3 from, Vector3 to)
        {
            if (box == null) return false;
            Vector3 a = box.transform.InverseTransformPoint(from), b = box.transform.InverseTransformPoint(to);
            var bounds = new Bounds(box.center, box.size + Vector3.one);
            if (bounds.Contains(a) || bounds.Contains(b)) return true;
            Vector3 segment = b - a;
            return segment.sqrMagnitude > .0001f && bounds.IntersectRay(new Ray(a, segment.normalized), out float distance) && distance <= segment.magnitude;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) && q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w > .5f && q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w < 1.5f;

        private void ServerReset(int id, Vector3 position)
        {
            var peer = peers.Values.FirstOrDefault(p => p.member.id == id);
            if (peer == null) return;
            int last = director.GetLastCheckpointIndex(id);
            if (last < 0) position = director.track.GetStartPosition(peer.member.slot, peers.Count) + Vector3.up * .5f;
            Vector3 direction = director.track.GetCheckpointPosition(Mathf.Clamp(last + 1, 0, director.track.FinishIndex)) - position;
            direction.y = 0;
            peer.position = position; peer.hasPose = false; peer.sequence = 0; peer.revision++;
            director.SetRacerPosition(id, position);
            peer.connection.Send(new ResetRider { round = Round, revision = peer.revision, position = position,
                rotation = direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction) : Quaternion.identity });
        }

        private void ServerFinished(int id) { BroadcastRace(); }

        public override void Update()
        {
            base.Update();
            double now = Time.realtimeSinceStartupAsDouble;
            if (NetworkServer.active)
            {
                foreach (int id in pending.Where(p => now >= p.Value).Select(p => p.Key).ToArray())
                { if (NetworkServer.connections.TryGetValue(id, out var conn)) conn.Disconnect(); pending.Remove(id); }
                if (now >= nextBroadcast && peers.Count > 0 && director.Phase != RacePhase.Lobby)
                { nextBroadcast = now + .2; BroadcastRace(); }
            }
            if (NetworkClient.active && !InRoom && connectionDeadline > 0 && now > connectionDeadline)
            { Status = "连接超时，请检查房主地址及 UDP 7777 端口。"; Leave(false); }
            if (LocalRider == null || !InRoom) return;
            var local = Race.rows != null ? Race.rows.FirstOrDefault(r => r.racerId == LocalId) : default;
            bool finished = local.status == RacerStatus.Finished || local.status == RacerStatus.Dnf || Race.phase == RacePhase.Finished;
            LocalRider.SetLocked(Race.phase != RacePhase.Racing || finished);
            if (Input.GetKeyDown(KeyCode.R) && !finished && Race.phase == RacePhase.Racing) RequestReset();
            double elapsed = local.elapsedSeconds + (!finished && Race.phase == RacePhase.Racing ? now - SnapshotReceivedAt : 0);
            trainingClock.ApplyNetworkClock(elapsed, finished);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            pending.Remove(conn.connectionId);
            if (peers.TryGetValue(conn.connectionId, out var peer))
            { peers.Remove(conn.connectionId); director.UnregisterRacer(peer.member.id); BroadcastRoom(); }
            base.OnServerDisconnect(conn);
        }

        public override void OnClientError(TransportError error, string reason) { Status = "连接失败：" + reason; }

        public override void OnStopClient()
        {
            ClearRiders(); HidePlayers();
            if (trainingClock != null) trainingClock.EndNetworkRun();
            LocalId = 0; connectionDeadline = 0; IsConnecting = false; Room = default; Race = default; Round = LocalRevision = 0;
            if (!leaving && string.IsNullOrEmpty(Status)) Status = "与房主的连接已断开。";
            else if (!leaving && !Status.Contains("失败") && !Status.Contains("超时") && !Status.Contains("不正确") && !Status.Contains("已满") && !Status.Contains("版本") && !Status.Contains("已开始")) Status = "与房主的连接已断开。";
            if (ui != null) ui.Screen = RaceNetworkUI.Page.Connect;
        }

        public override void OnStopServer() { peers.Clear(); pending.Clear(); if (director != null) { director.AbortToLobby(); director.enabled = false; } discovery.Stop(); }

        public void Leave(bool clearStatus = true)
        {
            leaving = true;
            discovery.Stop();
            if (NetworkServer.active && NetworkClient.active) StopHost();
            else if (NetworkClient.active) StopClient();
            else if (NetworkServer.active) StopServer();
            ClearRiders(); HidePlayers(); trainingClock.EndNetworkRun();
            LocalId = 0; Room = default; Race = default; connectionDeadline = 0; IsConnecting = false;
            if (clearStatus) Status = "已离开房间。";
            ui.Screen = RaceNetworkUI.Page.Connect;
        }

        private void ClearRiders()
        {
            if (LocalRider != null) { LocalRider.SetLocked(false); Destroy(LocalRider); LocalRider = null; }
            foreach (var view in views.Values) if (view != null) Destroy(view.gameObject);
            views.Clear();
        }

        public override void OnDestroy()
        {
            if (director != null) { director.RacerResetRequested -= ServerReset; director.RacerFinished -= ServerFinished; }
            ClearRiders();
            base.OnDestroy();
        }
    }
}
