using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Render the actual selection canvas at several sizes and exercise both button bindings.</summary>
[InitializeOnLoad]
public static class EquipmentSelectionValidation
{
    private const string Key = "Alpine.SelectionValidation";
    private static readonly StringBuilder Report = new StringBuilder();
    static EquipmentSelectionValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            {
                Time.captureDeltaTime = 1f / 60f;
                EditorApplication.update += Validate;
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

    private static void Validate()
    {
        if (Time.frameCount < 16) return;
        EditorApplication.update -= Validate;
        int code = 0;
        try
        {
            var selector = UnityEngine.Object.FindObjectOfType<GearSelect>();
            var canvas = selector.selectUIPanel.GetComponent<Canvas>();
            var layout = canvas.GetComponentInChildren<EquipmentSelectionLayout>();
            var single = layout.singleCard.GetComponent<Button>();
            var dual = layout.doubleCard.GetComponent<Button>();

            Require(single.onClick.GetPersistentEventCount() == 1 && dual.onClick.GetPersistentEventCount() == 1, "Both cards retain one persistent selection callback");
            Require(layout.GetComponentsInChildren<Graphic>().All(graphic => graphic.GetComponent<CanvasRenderer>() != null), "All text, surfaces and illustrations have CanvasRenderers");
            foreach (var label in layout.GetComponentsInChildren<Text>())
                foreach (char ch in label.text.Where(ch => !char.IsWhiteSpace(ch)))
                    RequireGlyph(label.font.HasCharacter(ch), label.text, ch);
            Report.AppendLine("PASS: Bundled font covers all UI text.");
            Capture(canvas, layout, "selection-1280x800", 1280, 800);
            Capture(canvas, layout, "selection-1920x1080", 1920, 1080);
            Capture(canvas, layout, "selection-portrait", 720, 1000);
            EventSystem.current.SetSelectedGameObject(single.gameObject);
            ExecuteEvents.Execute(single.gameObject, new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right }, ExecuteEvents.moveHandler);
            Require(EventSystem.current.currentSelectedGameObject == dual.gameObject, "Keyboard navigation moves between the two equipment cards");
            EventSystem.current.SetSelectedGameObject(null);
            ExecuteEvents.Execute(single.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            var feedback = single.GetComponent<EquipmentCardFeedback>();
            for (int i = 0; i < 15; i++) typeof(EquipmentCardFeedback).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(feedback, null);
            Require(single.transform.localScale.x > 1f, "Pointer hover highlights and raises the card");
            Capture(canvas, layout, "selection-hover", 1280, 800);
            ExecuteEvents.Execute(single.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Require(selector.boardSingle.activeSelf && !selector.boardDouble.activeSelf && !selector.selectUIPanel.activeSelf, "Single card starts the single-board character and closes selection");
            selector.boardSingle.SetActive(false);
            selector.selectUIPanel.SetActive(true);
            ExecuteEvents.Execute(dual.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            Require(!selector.boardSingle.activeSelf && selector.boardDouble.activeSelf && !selector.selectUIPanel.activeSelf, "Double card starts the double-board character and closes selection");
            Report.AppendLine("PASS: Selection rendering and interaction checks complete.");
        }
        catch (Exception error) { Report.AppendLine("FAIL: " + error); code = 1; }
        File.WriteAllText("Logs/equipment-selection-validation.txt", Report.ToString());
        Debug.Log(Report.ToString());
        SessionState.SetBool(Key, false);
        SessionState.SetInt(Key + ".Code", code);
        SessionState.SetBool(Key + ".Finish", true);
        EditorApplication.ExitPlaymode();
    }

    private static void Capture(Canvas canvas, EquipmentSelectionLayout layout, string name, int width, int height)
    {
        var camera = Camera.main;
        var overlayCamera = new GameObject("Selection Preview Camera").AddComponent<Camera>();
        overlayCamera.CopyFrom(camera);
        overlayCamera.clearFlags = CameraClearFlags.Depth;
        overlayCamera.cullingMask = 1 << 5;
        overlayCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        var scaler = canvas.GetComponent<CanvasScaler>();
        var mode = canvas.renderMode;
        var originalCamera = canvas.worldCamera;
        var originalScaleMode = scaler.uiScaleMode;
        float originalScale = scaler.scaleFactor;
        float originalAspect = camera.aspect;
        int originalMask = camera.cullingMask;
        var target = new RenderTexture(width, height, 24);
        camera.targetTexture = target;
        camera.cullingMask &= ~(1 << 5);
        camera.aspect = (float)width / height;
        overlayCamera.targetTexture = target;
        overlayCamera.aspect = camera.aspect;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = overlayCamera;
        canvas.planeDistance = 1f;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = Mathf.Sqrt((float)width / 1280f * height / 800f);
        canvas.scaleFactor = scaler.scaleFactor;
        Canvas.ForceUpdateCanvases();
        layout.Refresh();
        Canvas.ForceUpdateCanvases();
        foreach (var label in layout.GetComponentsInChildren<Text>())
        {
            if (label.preferredHeight > label.rectTransform.rect.height + 1f)
                Report.AppendLine("LAYOUT: " + name + " text height " + label.name + " needs=" + label.preferredHeight + " has=" + label.rectTransform.rect.height);
        }
        var corners = new Vector3[4];
        foreach (var card in new[] { layout.singleCard, layout.doubleCard })
        {
            card.GetWorldCorners(corners);
            foreach (var point in corners)
            {
                Vector3 screenPoint = overlayCamera.WorldToScreenPoint(point);
                if (screenPoint.x < -1f || screenPoint.x > width + 1f || screenPoint.y < -1f || screenPoint.y > height + 1f)
                    throw new Exception(name + " clips " + card.name);
            }
            var hits = new System.Collections.Generic.List<RaycastResult>();
            var pointer = new PointerEventData(EventSystem.current) { position = overlayCamera.WorldToScreenPoint(card.TransformPoint(card.rect.center)) };
            canvas.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            if (hits.Count == 0 || hits[0].gameObject != card.gameObject)
                throw new Exception(name + " card illustrations or background block pointer interaction");
        }
        Report.AppendLine("PASS: " + name + " fits both cards and routes pointer hits to the buttons.");
        camera.Render();
        overlayCamera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        image.Apply();
        Directory.CreateDirectory("Logs/SelectionPreview");
        File.WriteAllBytes("Logs/SelectionPreview/" + name + ".png", image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        camera.aspect = originalAspect;
        camera.cullingMask = originalMask;
        canvas.renderMode = mode;
        canvas.worldCamera = originalCamera;
        scaler.uiScaleMode = originalScaleMode;
        scaler.scaleFactor = originalScale;
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(overlayCamera.gameObject);
    }

    private static void RequireGlyph(bool condition, string text, char ch)
    {
        if (!condition) throw new Exception("Missing font glyph '" + ch + "' in " + text);
    }
    private static void Require(bool condition, string text)
    {
        if (!condition) throw new Exception(text);
        Report.AppendLine("PASS: " + text);
    }
}
