using UnityEngine;
using UnityEngine.UI;

/// <summary>Consistent native UI for weather, speed, controls and camera view.</summary>
public sealed class SkiGameplayHUD : MonoBehaviour
{
    public SkiWeatherController weather;
    public SkiRaceManager race;
    public CameraViewManager viewManager;
    public RectTransform canvasRect;
    public GameObject weatherPanel;
    public GameObject speedPanel;
    public GameObject viewPanel;
    public GameObject controlsPanel;
    public Text speedText;
    public SkiSpeedometerGraphic speedometer;
    public Text controlsText;
    public Text viewText;
    public Button[] weatherButtons;
    public Text[] weatherLabels;
    public WeatherSymbolGraphic[] weatherSymbols;
    public Color accent = new Color(.48f, .84f, 1f);
    public static SkiGameplayHUD Instance { get; private set; }
    public static bool IsPresent => Instance != null && Instance.isActiveAndEnabled;
    private SkiWeather lastStyledWeather = (SkiWeather)(-1);
    private static readonly string[] Views = { "追随视角", "鸟瞰视角", "近距视角", "第一人称" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() { Instance = null; }
    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void LateUpdate() { Refresh(); }

    public void Refresh()
    {
        bool running = race != null && race.IsRunning;
        bool finished = race != null && race.IsFinished;
        bool selecting = race != null && race.gearSelection != null && race.gearSelection.selectUIPanel.activeInHierarchy && !finished;
        if (weatherPanel != null) weatherPanel.SetActive(selecting);
        if (controlsPanel != null) controlsPanel.SetActive(selecting);
        if (speedPanel != null) speedPanel.SetActive(running && !selecting);
        if (viewPanel != null) viewPanel.SetActive(running && !selecting);
        if (running && !selecting)
        {
            var body = race.ActivePlayer != null ? race.ActivePlayer.GetComponent<Rigidbody>() : null;
            float speedKmh = (body != null ? body.velocity.magnitude : 0f) * 3.6f;
            if (speedometer != null) speedometer.SetSpeed(speedKmh);
            if (speedText != null) speedText.text = speedKmh.ToString("F1") + (speedometer == null ? " km/h" : "");
            if (viewText != null && viewManager != null)
            {
                viewText.text = Views[Mathf.Clamp(viewManager.CurrentViewIndex, 0, Views.Length - 1)] + "  ·  V";
            }
        }
        if (weather == null || weatherButtons == null || lastStyledWeather == weather.CurrentWeather) return;
        lastStyledWeather = weather.CurrentWeather;
        for (int i = 0; i < weatherButtons.Length; i++)
        {
            bool selected = i == (int)weather.CurrentWeather;
            var surface = weatherButtons[i].targetGraphic as AlpinePanelGraphic;
            Color ink = AlpineUITheme.Ink;
            if (surface != null)
            {
                surface.topColor = surface.bottomColor = selected ? AlpineUITheme.AccentSurface(accent) : AlpineUITheme.Inset;
                surface.borderColor = selected ? AlpineUITheme.Emphasis : AlpineUITheme.Border;
                surface.SetVerticesDirty();
            }
            weatherLabels[i].color = ink;
            weatherSymbols[i].color = ink;
        }
    }

    public void NextView()
    {
        if (race != null && race.IsRunning && viewManager != null) viewManager.NextView();
        Refresh();
    }
}
