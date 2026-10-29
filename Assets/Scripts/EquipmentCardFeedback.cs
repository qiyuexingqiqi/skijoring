using UnityEngine;
using UnityEngine.EventSystems;

public sealed class EquipmentCardFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public AlpinePanelGraphic surface;
    public Color accent;
    private bool pointerInside;
    private bool selected;
    private float blend;

    public void OnPointerEnter(PointerEventData data) { pointerInside = true; }
    public void OnPointerExit(PointerEventData data) { pointerInside = false; }
    public void OnSelect(BaseEventData data) { selected = true; }
    public void OnDeselect(BaseEventData data) { selected = false; }

    private void Update()
    {
        float target = pointerInside || selected ? 1f : 0f;
        blend = Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime * 7f);
        transform.localScale = Vector3.one * (1f + blend * 0.012f);
        surface.borderColor = new Color(accent.r, accent.g, accent.b, Mathf.Lerp(0.22f, 0.95f, blend));
        surface.SetVerticesDirty();
    }

    private void OnDisable()
    {
        pointerInside = selected = false;
        blend = 0f;
        transform.localScale = Vector3.one;
    }
}
