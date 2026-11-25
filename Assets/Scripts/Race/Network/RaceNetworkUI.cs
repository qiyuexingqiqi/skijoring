using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkiRace.Network
{
    public sealed class RaceNetworkUI : MonoBehaviour
    {
        public enum Page { Mode, Training, Connect, Lobby, Racing, Results }
        public Page Screen { get; set; }
        private SkiRaceNetworkManager manager;
        private Font font;
        private Canvas canvas;
        private RectTransform canvasRect;
        private GameObject mode, connect, lobby, results, racing, trainingBack;
        private RectTransform[] panels;
        private Text status, members, lobbyTitle, readyLabel, startLabel, leaderboard, raceTitle, countdown, resultRows, resultTitle, gearLabel, capacityLabel;
        private InputField nameInput, codeInput, addressInput;
        private Button readyButton, startButton, returnButton, createButton, joinButton, gearButton, capacityButton;
        private static Font sharedFont;
        private readonly Text[,] rankCells = new Text[8, 3];
        private readonly Text[,] resultCells = new Text[8, 4];

        public static Font ResolveFont(RaceNetworkAssets assets)
        {
            if (sharedFont == null) sharedFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 20);
            return sharedFont != null ? sharedFont : (assets != null && assets.fallbackFont != null ? assets.fallbackFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        }

        private void Start()
        {
            manager = GetComponent<SkiRaceNetworkManager>();
            font = ResolveFont(manager.assets);
            Build();
        }

        private void Build()
        {
            if (EventSystem.current == null) new GameObject("RaceEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var go = new GameObject("MultiplayerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 70;
            canvasRect = (RectTransform)go.transform;
            var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = .5f;

            var modePanel = FullScreen("Mode", out mode, 760, 440);
            Label(modePanel, "Title", "雪道竞速", 0, -46, 650, 54, 36, true);
            Label(modePanel, "Subtitle", "选择你的滑行方式", 0, -108, 650, 32, 18);
            Button(modePanel, "Training", "单人训练", -170, -216, 280, 90, manager.Train);
            Button(modePanel, "Multiplayer", "多人竞速", 170, -216, 280, 90, manager.Multiplayer);
            Label(modePanel, "Hint", "单板与双板  ·  同一局域网  ·  2–8 人竞速", 0, -358, 680, 30, 16);

            var connectPanel = FullScreen("Connect", out connect, 760, 650);
            Label(connectPanel, "Title", "多人竞速", 0, -28, 680, 48, 30, true);
            Label(connectPanel, "NameLabel", "玩家名称", -240, -100, 160, 30, 17);
            nameInput = Input(connectPanel, "Name", "输入昵称", 80, -100, 430, 40, 16);
            nameInput.text = PlayerPrefs.GetString("Race.PlayerName", "滑雪玩家");
            nameInput.onValueChanged.AddListener(value => manager.PlayerName = value); manager.PlayerName = nameInput.text;
            gearButton = Button(connectPanel, "Equipment", "装备：单板", -160, -164, 300, 44, () => { manager.DoubleBoard = !manager.DoubleBoard; });
            gearLabel = gearButton.GetComponentInChildren<Text>();
            capacityButton = Button(connectPanel, "Capacity", "房间上限：8 人", 160, -164, 300, 44, () => { manager.Capacity = manager.Capacity == 2 ? 3 : manager.Capacity == 3 ? 4 : manager.Capacity == 4 ? 8 : 2; });
            capacityLabel = capacityButton.GetComponentInChildren<Text>();
            createButton = Button(connectPanel, "Create", "创建房间", 0, -232, 620, 52, () => { SaveName(); manager.CreateRoom(); });
            Label(connectPanel, "JoinLabel", "加入已有房间", 0, -302, 650, 32, 18, true);
            codeInput = Input(connectPanel, "Code", "六位房间号", -160, -354, 300, 44, 6);
            codeInput.contentType = InputField.ContentType.IntegerNumber;
            joinButton = Button(connectPanel, "Join", "加入房间", 160, -354, 300, 44, () => { SaveName(); manager.JoinRoom(codeInput.text, addressInput.text); });
            addressInput = Input(connectPanel, "Address", "房主 IP（可选，留空自动搜索）", 0, -412, 620, 40, 64);
            status = Label(connectPanel, "Status", "", 0, -472, 650, 68, 16);
            Button(connectPanel, "Back", "返回模式选择", -160, -582, 300, 44, manager.Home);
            Button(connectPanel, "Cancel", "取消连接", 160, -582, 300, 44, () => manager.Leave());

            var lobbyPanel = FullScreen("Lobby", out lobby, 760, 650);
            lobbyTitle = Label(lobbyPanel, "Title", "", 0, -28, 690, 50, 28, true);
            Label(lobbyPanel, "Hint", "2 人起赛  ·  全员准备后由房主开始  ·  分享房间号邀请好友", 0, -88, 700, 30, 16);
            members = Label(lobbyPanel, "Members", "", 0, -148, 650, 334, 20);
            members.alignment = TextAnchor.UpperLeft; members.lineSpacing = 1.6f;
            readyButton = Button(lobbyPanel, "Ready", "准备", -160, -520, 300, 48, manager.ToggleReady);
            readyLabel = readyButton.GetComponentInChildren<Text>();
            startButton = Button(lobbyPanel, "Start", "房主开始", 160, -520, 300, 48, manager.StartRound);
            startLabel = startButton.GetComponentInChildren<Text>();
            Button(lobbyPanel, "Leave", "离开房间", 0, -586, 620, 40, () => manager.Leave());

            var resultPanel = FullScreen("Results", out results, 760, 650);
            resultTitle = Label(resultPanel, "Title", "本局成绩", 0, -28, 690, 50, 30, true);
            string[] headers = { "名次", "玩家", "状态", "用时" };
            float[] resultX = { -300, -140, 70, 240 }, resultWidth = { 60, 250, 110, 160 };
            for (int col = 0; col < 4; col++)
            {
                Label(resultPanel, "Header" + col, headers[col], resultX[col], -98, resultWidth[col], 30, 18, true);
                for (int row = 0; row < 8; row++)
                {
                    var cell = Label(resultPanel, "Row" + row + "Cell" + col, "", resultX[col], -150 - row * 38, resultWidth[col], 30, 19);
                    cell.resizeTextForBestFit = true; cell.resizeTextMinSize = 11; cell.resizeTextMaxSize = 19;
                    resultCells[row, col] = cell;
                }
            }
            returnButton = Button(resultPanel, "Room", "返回房间再来一局", -160, -552, 300, 50, manager.ReturnToRoom);
            Button(resultPanel, "Leave", "离开房间", 160, -552, 300, 50, () => manager.Leave());
            Label(resultPanel, "Hint", "成绩由房主统一记录  ·  R 复位每次加时 3 秒", 0, -612, 680, 28, 15);

            racing = Rect("Racing", canvas.transform).gameObject; Stretch((RectTransform)racing.transform);
            var rankPanel = Surface("Leaderboard", racing.transform, 350, 380);
            rankPanel.anchorMin = rankPanel.anchorMax = rankPanel.pivot = new Vector2(0, 1); rankPanel.anchoredPosition = new Vector2(20, -20);
            raceTitle = Label(rankPanel, "Title", "", 0, -14, 322, 40, 23, true);
            float[] rankX = { -144, -34, 112 }, rankWidth = { 30, 170, 90 };
            for (int row = 0; row < 8; row++) for (int col = 0; col < 3; col++)
            {
                var cell = Label(rankPanel, "Rank" + row + "Cell" + col, "", rankX[col], -74 - row * 32, rankWidth[col], 28, 17);
                cell.resizeTextForBestFit = true; cell.resizeTextMinSize = 10; cell.resizeTextMaxSize = 17;
                rankCells[row, col] = cell;
            }
            Label(rankPanel, "Hint", "R 返回上一检查点  ·  加时 3 秒", 0, -342, 322, 24, 14);
            countdown = Label((RectTransform)racing.transform, "Countdown", "", 0, 100, 700, 140, 32, true);
            countdown.rectTransform.anchorMin = countdown.rectTransform.anchorMax = new Vector2(.5f, .5f);
            var leaveRace = Button((RectTransform)racing.transform, "Leave", "退出房间", 0, 0, 150, 42, () => manager.Leave());
            var lr = (RectTransform)leaveRace.transform; lr.anchorMin = lr.anchorMax = new Vector2(1, 1); lr.anchoredPosition = new Vector2(-100, -40);

            trainingBack = Button(canvasRect, "ModeBack", "切换模式", 0, 0, 150, 42, manager.Home).gameObject;
            var tr = (RectTransform)trainingBack.transform; tr.anchorMin = tr.anchorMax = new Vector2(1, 1); tr.anchoredPosition = new Vector2(-100, -40);
            panels = new[] { modePanel, connectPanel, lobbyPanel, resultPanel };
        }

        private void SaveName() { manager.PlayerName = SkiRaceNetworkManager.CleanName(nameInput.text); PlayerPrefs.SetString("Race.PlayerName", manager.PlayerName); }

        private void LateUpdate()
        {
            if (canvas == null) return;
            mode.SetActive(Screen == Page.Mode); connect.SetActive(Screen == Page.Connect); lobby.SetActive(Screen == Page.Lobby);
            results.SetActive(Screen == Page.Results); racing.SetActive(Screen == Page.Racing);
            trainingBack.SetActive(Screen == Page.Training && manager.gear.selectUIPanel.activeInHierarchy);
            foreach (var panel in panels) panel.localScale = Vector3.one * Mathf.Min(1, (canvasRect.rect.width - 32) / panel.sizeDelta.x, (canvasRect.rect.height - 32) / panel.sizeDelta.y);
            status.text = manager.Status;
            gearLabel.text = "装备：" + (manager.DoubleBoard ? "双板" : "单板");
            capacityLabel.text = "房间上限：" + manager.Capacity + " 人";
            bool busy = manager.IsConnecting || NetworkClient.active || NetworkServer.active;
            createButton.interactable = joinButton.interactable = nameInput.interactable = codeInput.interactable = addressInput.interactable = gearButton.interactable = capacityButton.interactable = !busy;
            if (!manager.InRoom) return;
            var room = manager.Room;
            lobbyTitle.text = "房间 " + room.code + "    " + room.members.Length + " / " + room.capacity + " 人";
            members.text = string.Join("\n", room.members.Select(m => (m.id == manager.LocalId ? "▶ " : "   ") + m.name + (m.id == room.hostId ? "  [房主]" : "") + "    " + (m.doubleBoard ? "双板" : "单板") + "    " + (m.ready ? "已准备" : "等待准备")));
            var self = room.members.FirstOrDefault(m => m.id == manager.LocalId);
            readyLabel.text = self.ready ? "取消准备" : "准备";
            startButton.interactable = manager.IsRoomHost && room.members.Length >= 2 && room.members.All(m => m.ready);
            startLabel.text = manager.IsRoomHost ? (room.members.Length < 2 ? "等待至少 2 人" : "开始比赛") : "等待房主开始";
            returnButton.interactable = manager.IsRoomHost;
            var race = manager.Race;
            var rows = race.rows ?? new RaceRankRow[0];
            var local = rows.FirstOrDefault(r => r.racerId == manager.LocalId);
            raceTitle.text = "当前排名  " + local.rank + " / " + rows.Length;
            for (int row = 0; row < 8; row++)
            {
                bool active = row < rows.Length;
                for (int col = 0; col < 3; col++) rankCells[row, col].gameObject.SetActive(active);
                for (int col = 0; col < 4; col++) resultCells[row, col].gameObject.SetActive(active);
                if (!active) continue;
                var r = rows[row];
                Color ink = r.racerId == manager.LocalId ? AlpineUITheme.Emphasis : AlpineUITheme.Ink;
                for (int col = 0; col < 3; col++) rankCells[row, col].color = ink;
                for (int col = 0; col < 4; col++) resultCells[row, col].color = ink;
                rankCells[row, 0].text = resultCells[row, 0].text = r.rank.ToString();
                rankCells[row, 1].text = resultCells[row, 1].text = r.displayName;
                rankCells[row, 2].text = r.status == RacerStatus.Finished ? SkiRaceManager.FormatTime(r.elapsedSeconds)
                    : r.status == RacerStatus.Disconnected ? "已断线" : r.status == RacerStatus.Dnf ? "未完赛" : r.rank == 1 ? "领先" : "+" + r.distanceToLeader.ToString("F0") + " m";
                resultCells[row, 2].text = r.status == RacerStatus.Finished ? "完赛" : r.status == RacerStatus.Disconnected ? "已断线" : "未完赛";
                resultCells[row, 3].text = r.status == RacerStatus.Finished ? SkiRaceManager.FormatTime(r.elapsedSeconds) : "—";
            }
            countdown.text = race.phase == RacePhase.Countdown ? "准备出发\n" + Mathf.Max(1, Mathf.CeilToInt(race.countdown - (float)(Time.realtimeSinceStartupAsDouble - manager.SnapshotReceivedAt)))
                : local.status == RacerStatus.Finished ? "已完赛  " + SkiRaceManager.FormatTime(local.elapsedSeconds) + "\n等待其他选手冲线" : "";
            resultTitle.text = "本局成绩    " + rows.Count(r => r.status == RacerStatus.Finished) + " / " + rows.Length + " 人完赛";
        }

        private RectTransform FullScreen(string name, out GameObject root, float width, float height)
        {
            root = Rect(name, canvas.transform).gameObject; Stretch((RectTransform)root.transform);
            var backdrop = root.AddComponent<AlpinePanelGraphic>(); backdrop.topColor = AlpineUITheme.OverlayTop; backdrop.bottomColor = AlpineUITheme.OverlayBottom; backdrop.borderWidth = 0; backdrop.radius = 0; backdrop.raycastTarget = true;
            var panel = Surface("Panel", root.transform, width, height); panel.anchoredPosition = Vector2.zero;
            return panel;
        }

        private RectTransform Surface(string name, Transform parent, float width, float height)
        {
            var rect = Rect(name, parent); rect.sizeDelta = new Vector2(width, height);
            var surface = rect.gameObject.AddComponent<AlpinePanelGraphic>(); AlpineUITheme.ApplyPanel(surface); surface.radius = 16; surface.raycastTarget = true;
            return rect;
        }

        private Text Label(RectTransform parent, string name, string text, float x, float y, float width, float height, int size, bool bold = false)
        {
            var rect = Rect(name, parent); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
            var label = rect.gameObject.AddComponent<Text>(); label.font = font; label.text = text; label.fontSize = size;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal; label.color = AlpineUITheme.Ink; label.alignment = TextAnchor.MiddleCenter;
            label.supportRichText = false; label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private Button Button(RectTransform parent, string name, string text, float x, float y, float width, float height, UnityAction action)
        {
            var rect = Surface(name, parent, width, height); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(x, y);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<AlpinePanelGraphic>();
            var colors = button.colors; colors.highlightedColor = new Color(.84f, .94f, 1); colors.pressedColor = new Color(.64f, .84f, .95f); colors.disabledColor = new Color(.65f, .7f, .72f, .6f); button.colors = colors;
            var label = Label(rect, "Label", text, 0, 0, width - 12, height, 19, true); label.rectTransform.pivot = new Vector2(.5f, .5f); label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
            button.onClick.AddListener(action); return button;
        }

        private InputField Input(RectTransform parent, string name, string placeholder, float x, float y, float width, float height, int limit)
        {
            var rect = Surface(name, parent, width, height); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(x, y);
            var input = rect.gameObject.AddComponent<InputField>(); input.targetGraphic = rect.GetComponent<AlpinePanelGraphic>(); input.characterLimit = limit;
            var label = Label(rect, "Text", "", 0, 0, width - 24, height, 19); Stretch(label.rectTransform); label.rectTransform.offsetMin = new Vector2(12, 0); label.rectTransform.offsetMax = new Vector2(-12, 0); label.alignment = TextAnchor.MiddleLeft;
            var hint = Label(rect, "Placeholder", placeholder, 0, 0, width - 24, height, 18); Stretch(hint.rectTransform); hint.rectTransform.offsetMin = new Vector2(12, 0); hint.rectTransform.offsetMax = new Vector2(-12, 0); hint.color = AlpineUITheme.Muted; hint.alignment = TextAnchor.MiddleLeft;
            input.textComponent = label; input.placeholder = hint; return input;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); return rect;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, .5f); rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
