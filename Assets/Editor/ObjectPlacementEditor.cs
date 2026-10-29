using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ObjectPlacementHolder))]
public class ObjectPlacementEditor : Editor
{
    private ObjectPlacementHolder targetData;
    private GameObject previewInstance;
    private RaycastHit hitInfo;
    private bool isPlacing;

    private void OnEnable()
    {
        targetData = (ObjectPlacementHolder)target;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        DestroyPreview();
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        GUILayout.Space(10);

        if (GUILayout.Button("开始摆放物体", GUILayout.Height(30)))
        {
            isPlacing = true;
        }
        if (GUILayout.Button("停止摆放", GUILayout.Height(30)))
        {
            isPlacing = false;
            DestroyPreview();
        }

        EditorGUILayout.HelpBox(
@"操作：
拖拽彩色圆环Gizmo旋转物体（拖拽期间物体不再跟随鼠标）
松开鼠标后，物体继续跟随鼠标移动
T 重置旋转归零
鼠标左键 → 放置物体(Ctrl+Z撤销)
ESC → 退出摆放", MessageType.Info);
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (!isPlacing || targetData.prefabList == null || targetData.prefabList.Length == 0)
        {
            DestroyPreview();
            return;
        }

        int idx = targetData.currentPrefabIndex;
        if (idx < 0 || idx >= targetData.prefabList.Length) return;
        GameObject selectPrefab = targetData.prefabList[idx];
        if (selectPrefab == null) return;

        Event evt = Event.current;
        Camera cam = sceneView.camera;
        Vector2 mousePos = evt.mousePosition;
        mousePos.y = sceneView.camera.pixelHeight - mousePos.y;
        Ray ray = cam.ScreenPointToRay(mousePos);

        bool isDraggingHandle = GUIUtility.hotControl != 0;

        if (Physics.Raycast(ray, out hitInfo, Mathf.Infinity, targetData.groundLayer))
        {
            if (previewInstance == null)
            {
                previewInstance = Instantiate(selectPrefab);
                previewInstance.hideFlags = HideFlags.HideAndDontSave;
                SetPreviewTransparent(previewInstance, targetData.previewAlpha);
            }

            // ✅关键：没有拖拽Gizmo的时候，才跟随鼠标更新位置
            if (!isDraggingHandle)
            {
                previewInstance.transform.position = hitInfo.point;
            }

            // 原生旋转Gizmo
            EditorGUI.BeginChangeCheck();
            Quaternion newRot = Handles.RotationHandle(previewInstance.transform.rotation, previewInstance.transform.position);
            if (EditorGUI.EndChangeCheck())
            {
                previewInstance.transform.rotation = newRot;
            }

            // 按键
            if (evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.T)
                {
                    previewInstance.transform.localRotation = Quaternion.identity;
                    evt.Use();
                }
                if (evt.keyCode == KeyCode.Escape)
                {
                    isPlacing = false;
                    DestroyPreview();
                    evt.Use();
                }
            }

            // 左键放置物体
            if (evt.type == EventType.MouseDown && evt.button == 0 && !isDraggingHandle)
            {
                GameObject newObj = PrefabUtility.InstantiatePrefab(selectPrefab) as GameObject;
                newObj.transform.position = previewInstance.transform.position;
                newObj.transform.rotation = previewInstance.transform.rotation;
                Undo.RegisterCreatedObjectUndo(newObj, "放置物体");
                evt.Use();
            }
        }
        else
        {
            if (!isDraggingHandle)
            {
                DestroyPreview();
            }
        }

        sceneView.Repaint();
    }

    void DestroyPreview()
    {
        if (previewInstance != null)
        {
            DestroyImmediate(previewInstance);
            previewInstance = null;
        }
    }

    void SetPreviewTransparent(GameObject obj, float alpha)
    {
        var renders = obj.GetComponentsInChildren<Renderer>();
        foreach (var r in renders)
        {
            Color c = r.sharedMaterial.color;
            c.a = alpha;
            r.sharedMaterial.color = c;
        }
    }
}
