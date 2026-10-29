using UnityEngine;

[ExecuteAlways]
public sealed class EquipmentSelectionLayout : MonoBehaviour
{
    public RectTransform content;
    public RectTransform singleCard;
    public RectTransform doubleCard;
    public RectTransform footer;
    public RectTransform weatherPanel;
    public RectTransform controlsPanel;
    private Vector2 lastSize;

    private void OnEnable() { Refresh(); }
    private void Update()
    {
        Vector2 size = ((RectTransform)transform).rect.size;
        if (size != lastSize) Refresh();
    }

    public void Refresh()
    {
        if (content == null || singleCard == null || doubleCard == null) return;
        lastSize = ((RectTransform)transform).rect.size;
        float width = Mathf.Min(800f, Mathf.Max(200f, lastSize.x - 96f));
        bool stacked = lastSize.y > lastSize.x * 1.05f || width < 720f;
        if (stacked) width = Mathf.Min(480f, Mathf.Max(200f, lastSize.x - 64f));
        bool hasSettings = weatherPanel != null && controlsPanel != null;
        float height = hasSettings ? (stacked ? 1118f : 628f) : (stacked ? 850f : 490f);
        content.sizeDelta = new Vector2(width, height);
        content.localScale = Vector3.one * Mathf.Min(1f, Mathf.Max(0.1f, (lastSize.y - 48f) / height));
        float cardWidth = stacked ? width : (width - 24f) * 0.5f;
        SetCard(singleCard, 0f, -86f, cardWidth);
        SetCard(doubleCard, stacked ? 0f : cardWidth + 24f, stacked ? -448f : -86f, cardWidth);
        if (hasSettings)
        {
            SetSettings(weatherPanel, 0f, stacked ? -810f : -448f, cardWidth);
            SetSettings(controlsPanel, stacked ? 0f : cardWidth + 24f, stacked ? -938f : -448f, cardWidth);
        }
        footer.anchoredPosition = new Vector2(0f, hasSettings ? (stacked ? -1074f : -584f) : (stacked ? -812f : -452f));
    }

    private static void SetCard(RectTransform card, float x, float y, float width)
    {
        card.anchoredPosition = new Vector2(x, y);
        card.sizeDelta = new Vector2(width, 338f);
    }

    private static void SetSettings(RectTransform panel, float x, float y, float width)
    {
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(x, y);
        panel.sizeDelta = new Vector2(width, 112f);
    }
}
