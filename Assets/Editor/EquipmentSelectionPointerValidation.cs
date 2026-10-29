using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Exercise real Overlay-canvas pointer down/up over multiple rendered frames.</summary>
[InitializeOnLoad]
public static class EquipmentSelectionPointerValidation
{
    private const string Key = "Alpine.PointerValidation";
    private static readonly StringBuilder Report = new StringBuilder();
    private static IEnumerator routine;
    private static int frame = -1;
    static EquipmentSelectionPointerValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                Time.captureDeltaTime = 1f / 60f;
                EditorApplication.update += Tick;
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + ".Finish", false))
            {
                SessionState.SetBool(Key + ".Finish", false);
                EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
                EditorApplication.Exit(SessionState.GetInt(Key + ".Code", 1));
            }
        };
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene(SnowboardIntegrationBuild.ScenePath);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (Time.frameCount < 20 || Time.frameCount == frame) return;
        frame = Time.frameCount;
        var events = EventSystem.current;
        if (events != null) typeof(EventSystem).GetField("m_HasFocus", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(events, true);
        try
        {
            if (routine == null) routine = Exercise();
            if (routine.MoveNext()) return;
            Finish(0);
        }
        catch (Exception error)
        {
            Report.AppendLine("FAIL: " + error);
            Finish(1);
        }
    }
    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        File.WriteAllText("Logs/equipment-selection-pointer-validation.txt", Report.ToString());
        Debug.Log(Report.ToString());
        SessionState.SetBool(Key, false);
        SessionState.SetInt(Key + ".Code", code);
        SessionState.SetBool(Key + ".Finish", true);
        EditorApplication.ExitPlaymode();
    }
    private static IEnumerator Exercise()
    {
        var selector = UnityEngine.Object.FindObjectOfType<GearSelect>();
        var layout = selector.selectUIPanel.GetComponentInChildren<EquipmentSelectionLayout>();
        var events = EventSystem.current;
        var module = events.GetComponent<StandaloneInputModule>();
        var input = events.gameObject.AddComponent<EquipmentSelectionPointerInput>();
        module.inputOverride = input;
        module.forceModuleActive = true;
        input.position = new Vector2(-100f, -100f);
        Report.AppendLine("Overlay screen=" + Screen.width + "x" + Screen.height);
        foreach (var card in new[] { layout.doubleCard, layout.singleCard })
        {
            foreach (string part in new[] { "Equipment Name", "Action", "Equipment Illustration" })
            {
                selector.boardSingle.SetActive(false);
                selector.boardDouble.SetActive(false);
                selector.selectUIPanel.SetActive(true);
                events.SetSelectedGameObject(null);
                input.down = input.held = input.up = false;
                yield return null;
                yield return null;
                yield return null;
                var target = (RectTransform)card.Find(part);
                input.position = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center));
                yield return null;
                yield return null;
                var hits = new List<RaycastResult>();
                events.RaycastAll(new PointerEventData(events) { position = input.position }, hits);
                Report.AppendLine(card.name + "/" + part + " " + input.position + " hits=" + string.Join(",", hits.Take(4).Select(h => h.gameObject.name)));
                input.down = input.held = true;
                yield return null;
                input.down = false;
                yield return null;
                input.held = false;
                input.up = true;
                yield return null;
                input.up = false;
                yield return null;
                yield return null;
                bool dual = card == layout.doubleCard;
                if (selector.selectUIPanel.activeSelf || selector.boardDouble.activeSelf != dual || selector.boardSingle.activeSelf == dual)
                    throw new Exception("Mouse down/up did not select " + card.name + "/" + part);
                Report.AppendLine("PASS: actual pointer down/up selected " + card.name + "/" + part);
            }
        }
        module.inputOverride = null;
        UnityEngine.Object.Destroy(input);
        Report.AppendLine("PASS: all real Overlay pointer selection checks.");
    }
}
