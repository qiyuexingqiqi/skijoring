using UnityEngine;

/// <summary>
/// 视角切换 UI
/// 挂载位置：B3_FollowCamera 同一 GameObject（与 CameraViewManager 一起）
/// 功能：按 V 键循环切换视角 + 右下角按钮点击切换
/// </summary>
public class CameraViewSwitcherUI : MonoBehaviour
{
    [Header("=== 键盘设置 ===")]
    [SerializeField] private KeyCode switchKey = KeyCode.V;

    [Header("=== 右下角按钮 ===")]
    [SerializeField] private float buttonWidth = 160f;
    [SerializeField] private float buttonHeight = 45f;

    private CameraViewManager viewManager;
    private GearSelect gearSelection;
    private SkiRaceManager race;
    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private bool stylesInitialized;

    void Start()
    {
        viewManager = GetComponent<CameraViewManager>();
        gearSelection = FindObjectOfType<GearSelect>();
        race = FindObjectOfType<SkiRaceManager>();
        if (viewManager == null)
        {
            Debug.LogWarning("[CameraViewSwitcherUI] 未找到 CameraViewManager");
        }
    }

    void Update()
    {
        if (race != null && race.IsFinished) return;
        if (Input.GetKeyDown(switchKey) && viewManager != null)
        {
            viewManager.NextView();
        }
    }

    void OnGUI()
    {
        if (SkiGameplayHUD.IsPresent) return;
        if (race != null && race.IsFinished) return;
        if (gearSelection != null && gearSelection.selectUIPanel != null && gearSelection.selectUIPanel.activeInHierarchy) return;
        if (viewManager == null) return;

        InitializeStyles();

        float x = Screen.width - buttonWidth - 20f;
        float y = Screen.height - buttonHeight - 20f;

        GUI.Label(new Rect(x, y - 22f, buttonWidth, 20f), GetViewLabel(), labelStyle);

        if (GUI.Button(new Rect(x, y, buttonWidth, buttonHeight), GetButtonText(), buttonStyle))
        {
            viewManager.NextView();
        }
    }

    private void InitializeStyles()
    {
        if (stylesInitialized) return;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 16;
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.yellow;
        buttonStyle.active.textColor = Color.green;
        buttonStyle.alignment = TextAnchor.MiddleCenter;

        buttonStyle.normal.background = MakeTex(new Color(0.1f, 0.1f, 0.2f, 0.7f));
        buttonStyle.hover.background = MakeTex(new Color(0.2f, 0.2f, 0.4f, 0.8f));
        buttonStyle.active.background = MakeTex(new Color(0.15f, 0.3f, 0.15f, 0.8f));

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 13;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.normal.textColor = new Color(0.8f, 0.9f, 1f, 0.9f);
        labelStyle.alignment = TextAnchor.MiddleRight;

        stylesInitialized = true;
    }

    private Texture2D MakeTex(Color color)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    private string GetButtonText()
    {
        string currentName = viewManager.CurrentViewName;
        if (currentName.Contains(" "))
            currentName = currentName.Substring(0, currentName.IndexOf(' '));
        return $"\u25c9 {currentName}";
    }

    private string GetViewLabel()
    {
        int total = viewManager.ViewCount;
        int current = viewManager.CurrentViewIndex + 1;
        return $"视角 [{current}/{total}]  按 {switchKey} 切换";
    }
}
