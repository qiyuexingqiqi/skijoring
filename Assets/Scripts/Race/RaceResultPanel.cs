using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkiRace
{
    /// <summary>
    /// 正式结算面板：读取 RaceDirector.Results，显示名次、名字、状态、用时，
    /// 高亮本地玩家，并提供“再来一局 / 返回选择”。
    /// 运行时自动创建 UGUI，使用现有 AlpineUITheme 的冰蓝半透明风格。
    /// </summary>
    public sealed class RaceResultPanel : MonoBehaviour
    {
        public RaceDirector director;
        public RaceSessionController session;

        private sealed class RowUI
        {
            public GameObject root;
            public Text rank;
            public Text name;
            public Text status;
            public Text time;
        }

        private readonly List<RowUI> rows = new List<RowUI>();
        private Font font;
        private Canvas canvas;
        private GameObject root;
        private bool built;
        private bool shown;

        private void Start()
        {
            if (director == null) director = RaceDirector.Instance;
            if (session == null) session = FindObjectOfType<RaceSessionController>(true);
            Build();
            Hide();
        }

        private void Update()
        {
            if (director == null) return;
            bool finished = director.Phase == RacePhase.Finished;
            if (finished && !shown) Show();
            else if (!finished && shown) Hide();
        }

        public void Show()
        {
            Build();
            if (root == null) return;
            Refresh();
            root.SetActive(true);
            shown = true;
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
            shown = false;
        }

        private void Build()
        {
            if (built) return;
            built = true;

            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Arial" }, 16);
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (EventSystem.current == null)
                new GameObject("RaceResultEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasObject = new GameObject("RaceResultCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 800f);
            scaler.matchWidthOrHeight = 0.5f;

            var overlay = CreateRect("Root", canvasObject.transform);
            Stretch(overlay, 0f, 0f, 0f, 0f);
            root = overlay.gameObject;

            var backdrop = CreateRect("Backdrop", overlay);
            Stretch(backdrop, 0f, 0f, 0f, 0f);
            var backdropGraphic = backdrop.gameObject.AddComponent<AlpinePanelGraphic>();
            backdropGraphic.topColor = AlpineUITheme.OverlayTop;
            backdropGraphic.bottomColor = AlpineUITheme.OverlayBottom;
            backdropGraphic.borderColor = Color.clear;
            backdropGraphic.radius = 0f;
            backdropGraphic.borderWidth = 0f;
            backdropGraphic.raycastTarget = true;

            var panel = CreateRect("Panel", overlay);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(640f, 500f);
            panel.anchoredPosition = Vector2.zero;
            var panelGraphic = panel.gameObject.AddComponent<AlpinePanelGraphic>();
            AlpineUITheme.ApplyPanel(panelGraphic);
            panelGraphic.radius = 18f;
            panelGraphic.borderWidth = 1f;
            panelGraphic.raycastTarget = true;

            CreateText(panel, "Title", "比赛结算", new Vector2(0f, -28f), new Vector2(600f, 44f),
                30, AlpineUITheme.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);

            var header = CreateRect("Header", panel);
            header.anchorMin = header.anchorMax = header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = new Vector2(0f, -88f);
            header.sizeDelta = new Vector2(600f, 26f);
            CreateRowText(header, "Rank", "名次", 20f, 70f, AlpineUITheme.Muted, FontStyle.Bold);
            CreateRowText(header, "Name", "选手", 100f, 240f, AlpineUITheme.Muted, FontStyle.Bold);
            CreateRowText(header, "Status", "状态", 360f, 100f, AlpineUITheme.Muted, FontStyle.Bold);
            CreateRowText(header, "Time", "用时", 500f, 110f, AlpineUITheme.Muted, FontStyle.Bold);

            for (int i = 0; i < 8; i++)
            {
                var rowRoot = CreateRect("Row_" + i, panel);
                rowRoot.anchorMin = rowRoot.anchorMax = rowRoot.pivot = new Vector2(0.5f, 1f);
                rowRoot.anchoredPosition = new Vector2(0f, -126f - i * 36f);
                rowRoot.sizeDelta = new Vector2(600f, 32f);

                var row = new RowUI
                {
                    root = rowRoot.gameObject,
                    rank = CreateRowText(rowRoot, "Rank", "—", 20f, 70f, AlpineUITheme.Ink),
                    name = CreateRowText(rowRoot, "Name", "—", 100f, 240f, AlpineUITheme.Ink),
                    status = CreateRowText(rowRoot, "Status", "—", 360f, 100f, AlpineUITheme.Ink),
                    time = CreateRowText(rowRoot, "Time", "—", 500f, 110f, AlpineUITheme.Ink)
                };
                rows.Add(row);
            }

            CreateButton(panel, "Rematch", "再来一局", new Vector2(-110f, 26f),
                AlpineUITheme.AccentSurface(new Color(0.48f, 0.84f, 1f)),
                () => { if (session != null) session.Rematch(); });
            CreateButton(panel, "Return", "返回选择", new Vector2(110f, 26f),
                AlpineUITheme.Inset,
                () => { if (session != null) session.ReturnToSelection(); });
        }

        private void Refresh()
        {
            var results = director != null ? director.Results : null;
            int count = results != null ? results.Count : 0;

            for (int i = 0; i < rows.Count; i++)
            {
                bool active = i < count;
                rows[i].root.SetActive(active);
                if (!active) continue;

                var row = results[i];
                rows[i].rank.text = row.rank.ToString();
                rows[i].name.text = row.displayName;
                rows[i].status.text = StatusText(row.status);
                rows[i].time.text = TimeText(row);
                rows[i].time.color = row.status == RacerStatus.Dnf ? AlpineUITheme.Muted : AlpineUITheme.Ink;

                Color ink = row.isLocal
                    ? AlpineUITheme.Emphasis
                    : (row.status == RacerStatus.Dnf ? AlpineUITheme.Muted : AlpineUITheme.Ink);
                rows[i].rank.color = ink;
                rows[i].name.color = ink;
                rows[i].status.color = ink;
            }
        }

        private static string StatusText(RacerStatus status)
        {
            switch (status)
            {
                case RacerStatus.Finished: return "完赛";
                case RacerStatus.Racing: return "滑行中";
                case RacerStatus.Dnf: return "未完赛";
                case RacerStatus.Disconnected: return "掉线";
                default: return "等待";
            }
        }

        private static string TimeText(RaceRankRow row)
        {
            if (row.status == RacerStatus.Dnf || row.status == RacerStatus.Disconnected)
                return "DNF";
            return FormatTime(row.elapsedSeconds);
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

        private Text CreateText(Transform parent, string name, string content, Vector2 position,
            Vector2 size, int fontSize, Color color, FontStyle style, TextAnchor anchor)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Text CreateRowText(Transform parent, string name, string content, float x, float width,
            Color color, FontStyle style = FontStyle.Normal)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, 32f);

            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = 16;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private Button CreateButton(Transform parent, string name, string label, Vector2 position,
            Color fill, UnityAction action)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(190f, 46f);

            var graphic = rect.gameObject.AddComponent<AlpinePanelGraphic>();
            graphic.topColor = fill;
            graphic.bottomColor = fill;
            graphic.borderColor = AlpineUITheme.Border;
            graphic.radius = 10f;
            graphic.borderWidth = 1f;
            graphic.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.ColorTint;

            var text = CreateText(rect, "Label", label, Vector2.zero, new Vector2(190f, 46f),
                17, AlpineUITheme.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
            text.verticalOverflow = VerticalWrapMode.Truncate;

            button.onClick.AddListener(action);
            return button;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
