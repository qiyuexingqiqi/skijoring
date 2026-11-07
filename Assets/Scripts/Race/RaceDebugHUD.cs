using UnityEngine;

namespace SkiRace
{
    /// <summary>
    /// 离线比赛的调试面板：启动 8 人假对手、实时排行榜、结束比赛。
    /// 只在测试对象上使用，不参与正式 HUD。
    /// </summary>
    public sealed class RaceDebugHUD : MonoBehaviour
    {
        public RaceDirector director;
        public LocalRaceSimulator simulator;
        public bool visible = true;
        public KeyCode toggleKey = KeyCode.F8;

        private Font font;
        private GUIStyle titleStyle;
        private GUIStyle textStyle;
        private GUIStyle smallStyle;
        private GUIStyle buttonStyle;
        private GUIStyle boxStyle;
        private bool stylesReady;

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
            if (Input.GetKeyDown(KeyCode.F9) && simulator != null) simulator.StartDemo();
        }

        private void OnGUI()
        {
            if (!visible || director == null) return;
            EnsureStyles();

            const float width = 380f;
            const float height = 480f;
            Rect panel = new Rect(Screen.width - width - 16f, 16f, width, height);
            GUI.Box(panel, GUIContent.none, boxStyle);

            GUI.Label(new Rect(panel.x + 16f, panel.y + 12f, width - 32f, 26f),
                "离线竞速测试  ·  F8 隐藏", titleStyle);

            double elapsed = director.CurrentRaceTime;
            if (director.Phase == RacePhase.Finished && director.Results.Count > 0)
                elapsed = director.Results[0].elapsedSeconds;
            string phaseText = PhaseText(director.Phase);
            if (director.Phase == RacePhase.Countdown)
                phaseText += "  " + director.CountdownRemaining.ToString("F1") + "s";
            else if (director.Phase == RacePhase.Racing || director.Phase == RacePhase.Finished)
                phaseText += "  " + FormatTime(elapsed);

            GUI.Label(new Rect(panel.x + 16f, panel.y + 42f, width - 32f, 22f),
                phaseText + "  ·  选手 " + director.RacerCount, smallStyle);

            float buttonY = panel.y + 72f;
            if (GUI.Button(new Rect(panel.x + 16f, buttonY, 150f, 30f), "开始 / 重开离线比赛", buttonStyle))
            {
                if (simulator != null) simulator.StartDemo();
            }
            if (GUI.Button(new Rect(panel.x + 174f, buttonY, 100f, 30f), "结束比赛", buttonStyle))
            {
                director.ForceFinishRace();
            }
            if (GUI.Button(new Rect(panel.x + 282f, buttonY, 82f, 30f), "隐藏", buttonStyle))
            {
                visible = false;
            }

            float y = panel.y + 116f;
            GUI.Label(new Rect(panel.x + 16f, y, width - 32f, 20f),
                "名次    选手              状态        用时 / 进度", smallStyle);
            y += 24f;

            var rows = director.Phase == RacePhase.Finished ? director.Results : director.Ranking;
            if (rows == null || rows.Count == 0)
            {
                GUI.Label(new Rect(panel.x + 16f, y, width - 32f, 24f),
                    "点击“开始”生成 7 名虚拟选手（可选本地玩家）", textStyle);
                return;
            }

            for (int i = 0; i < rows.Count && i < 8; i++)
            {
                var row = rows[i];
                bool local = row.isLocal;
                GUI.color = local ? new Color(0.48f, 0.92f, 1f) : Color.white;

                GUI.Label(new Rect(panel.x + 16f, y, 42f, 22f), row.rank.ToString(), textStyle);
                GUI.Label(new Rect(panel.x + 58f, y, 128f, 22f), ShortName(row.displayName), textStyle);
                GUI.Label(new Rect(panel.x + 186f, y, 66f, 22f), StatusText(row.status), textStyle);

                string value;
                if (row.status == RacerStatus.Finished)
                    value = FormatTime(row.elapsedSeconds);
                else if (row.status == RacerStatus.Racing)
                    value = Mathf.RoundToInt(row.progress * 100f) + "%  " + FormatTime(row.elapsedSeconds);
                else
                    value = "—";
                GUI.Label(new Rect(panel.x + 252f, y, width - 268f, 22f), value, textStyle);

                y += 24f;
            }

            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 16f, panel.y + height - 34f, width - 32f, 20f),
                "R 复位到最近检查点并加罚时  ·  F9 重开", smallStyle);
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "—";
            return name.Length <= 8 ? name : name.Substring(0, 8);
        }

        private static string PhaseText(RacePhase phase)
        {
            switch (phase)
            {
                case RacePhase.Countdown: return "倒计时";
                case RacePhase.Racing: return "比赛中";
                case RacePhase.Finished: return "已结束";
                default: return "准备";
            }
        }

        private static string StatusText(RacerStatus status)
        {
            switch (status)
            {
                case RacerStatus.Racing: return "滑行";
                case RacerStatus.Finished: return "完赛";
                case RacerStatus.Dnf: return "未完赛";
                case RacerStatus.Disconnected: return "掉线";
                default: return "等待";
            }
        }

        private static string FormatTime(double seconds)
        {
            if (seconds <= 0d) return "00:00.00";
            int centiseconds = Mathf.FloorToInt((float)(seconds * 100d));
            int minutes = centiseconds / 6000;
            int secs = centiseconds / 100 % 60;
            int cs = centiseconds % 100;
            return $"{minutes:00}:{secs:00}.{cs:00}";
        }

        private void EnsureStyles()
        {
            if (stylesReady && font != null) return;
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Arial" }, 14);

            titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.font = font;
            titleStyle.fontSize = 16;
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.normal.textColor = Color.white;

            textStyle = new GUIStyle(GUI.skin.label);
            textStyle.font = font;
            textStyle.fontSize = 13;
            textStyle.normal.textColor = Color.white;

            smallStyle = new GUIStyle(textStyle);
            smallStyle.fontSize = 12;
            smallStyle.normal.textColor = new Color(0.82f, 0.9f, 1f);

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.font = font;
            buttonStyle.fontSize = 13;

            boxStyle = new GUIStyle(GUI.skin.box);
            stylesReady = true;
        }
    }
}
