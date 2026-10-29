using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 长下坡地形生成器 - 简化版
/// 一键生成多块拼接的长直下坡地形
/// </summary>
public class LongSlopeGenerator : EditorWindow
{
    [Header("地形尺寸")]
    private int tileCount = 4;
    private float tileWidth = 200f;
    private float tileLength = 500f;
    private float tileHeight = 100f;
    private int heightmapResolution = 257;

    [Header("坡度")]
    private float slopeAngle = 5f;
    private float startHeight = 80f;

    [Header("表面")]
    private float bumpiness = 0.5f;

    [MenuItem("Tools/Skiing/Long Slope Generator")]
    static void OpenWindow()
    {
        GetWindow<LongSlopeGenerator>("Long Slope Generator");
    }

    void OnGUI()
    {
        GUILayout.Label("长下坡地形生成器", EditorStyles.largeLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "生成多块地形沿 Z 轴拼接，形成长直下坡赛道。\n" +
            "每块地形有独立 TerrainData，接缝自动对齐。",
            MessageType.Info
        );

        EditorGUILayout.Space();

        // 地形尺寸
        EditorGUILayout.LabelField("地形尺寸", EditorStyles.boldLabel);
        tileCount = EditorGUILayout.IntSlider("块数", tileCount, 2, 10);
        tileWidth = EditorGUILayout.FloatField("每块宽度", tileWidth);
        tileLength = EditorGUILayout.FloatField("每块长度", tileLength);
        tileHeight = EditorGUILayout.FloatField("最大高度", tileHeight);
        heightmapResolution = EditorGUILayout.IntPopup(
            "高度图精度",
            heightmapResolution,
            new string[] { "129", "257", "513", "1025" },
            new int[] { 129, 257, 513, 1025 }
        );

        float totalLength = tileCount * tileLength;
        EditorGUILayout.LabelField($"总长度: {totalLength:F0} 米", EditorStyles.boldLabel);

        EditorGUILayout.Space();

        // 坡度
        EditorGUILayout.LabelField("坡度", EditorStyles.boldLabel);
        slopeAngle = EditorGUILayout.Slider("坡度角度", slopeAngle, 0f, 20f);
        startHeight = EditorGUILayout.Slider("起始高度", startHeight, 0f, tileHeight);

        float dropPerTile = tileLength * Mathf.Tan(slopeAngle * Mathf.Deg2Rad);
        float endHeight = Mathf.Max(0f, startHeight - tileCount * dropPerTile);
        EditorGUILayout.HelpBox(
            $"每块落差: {dropPerTile:F1}m\n" +
            $"坡顶: {startHeight:F1}m → 坡底: {endHeight:F1}m\n" +
            $"总落差: {startHeight - endHeight:F1}m",
            MessageType.None
        );

        EditorGUILayout.Space();

        // 表面
        EditorGUILayout.LabelField("表面", EditorStyles.boldLabel);
        bumpiness = EditorGUILayout.Slider("粗糙度", bumpiness, 0f, 3f);

        EditorGUILayout.Space();

        // 生成按钮
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
        if (GUILayout.Button("生成长下坡地形", GUILayout.Height(45)))
        {
            Generate();
        }
        GUI.backgroundColor = Color.white;
    }

    void Generate()
    {
        // 计算每块的起始/结束高度
        float[] startHeights = new float[tileCount];
        float[] endHeights = new float[tileCount];

        float currentH = startHeight;
        float drop = tileLength * Mathf.Tan(slopeAngle * Mathf.Deg2Rad);

        for (int i = 0; i < tileCount; i++)
        {
            startHeights[i] = currentH;
            endHeights[i] = currentH - drop;
            currentH = endHeights[i];
        }

        // 如果有负高度，整体抬高
        float minH = float.MaxValue;
        for (int i = 0; i < tileCount; i++)
        {
            if (startHeights[i] < minH) minH = startHeights[i];
            if (endHeights[i] < minH) minH = endHeights[i];
        }

        float offset = 0f;
        if (minH < 0f)
        {
            offset = -minH + 5f;
            for (int i = 0; i < tileCount; i++)
            {
                startHeights[i] += offset;
                endHeights[i] += offset;
            }
        }

        float actualMaxHeight = Mathf.Max(tileHeight, startHeights[0] + 10f);

        // 创建父对象
        GameObject parent = new GameObject("LongSlopeTerrain");
        Undo.RegisterCreatedObjectUndo(parent, "Create Long Slope");

        // 逐块生成
        for (int i = 0; i < tileCount; i++)
        {
            CreateTile(i, startHeights[i], endHeights[i], actualMaxHeight, parent);
        }

        Selection.activeGameObject = parent;

        EditorUtility.DisplayDialog(
            "完成",
            $"已生成 {tileCount} 块地形，总长度 {tileCount * tileLength:F0}m。\n" +
            $"请将玩家拖到起点 (Z≈0, Y≈{startHeights[0] + 2:F0}) 测试。",
            "确定"
        );
    }

    void CreateTile(int index, float startH, float endH, float maxH, GameObject parent)
    {
        // 创建 TerrainData
        TerrainData td = new TerrainData();
        td.heightmapResolution = heightmapResolution;
        td.size = new Vector3(tileWidth, maxH, tileLength);

        int res = td.heightmapResolution;
        float[,] heights = new float[res, res];

        for (int z = 0; z < res; z++)
        {
            float t = (float)z / (res - 1);
            float baseH = Mathf.Lerp(startH, endH, t);

            for (int x = 0; x < res; x++)
            {
                float h = baseH;

                if (bumpiness > 0f)
                {
                    float noise = Mathf.PerlinNoise(x * 0.1f + index * 100f, z * 0.1f) - 0.5f;
                    h += noise * bumpiness * 0.01f * maxH;
                }

                heights[z, x] = Mathf.Clamp01(h / maxH);
            }
        }

        td.SetHeights(0, 0, heights);

        // 保存资源
        string path = $"Assets/TerrainData/SlopeTile_{index}.asset";
        EnsureFolder("Assets/TerrainData");
        AssetDatabase.CreateAsset(td, path);

        // 创建 Terrain 对象
        GameObject obj = Terrain.CreateTerrainGameObject(td);
        obj.name = $"SlopeTile_{index}";
        obj.transform.position = new Vector3(0f, 0f, index * tileLength);
        obj.transform.SetParent(parent.transform, false);

        // 碰撞体
        TerrainCollider tc = obj.GetComponent<TerrainCollider>();
        if (tc != null) tc.terrainData = td;

        Terrain terrain = obj.GetComponent<Terrain>();
        terrain.basemapDistance = 1000f;

        Undo.RegisterCreatedObjectUndo(obj, $"Create Tile {index}");
    }

    void EnsureFolder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string folder = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent)) parent = "Assets";
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
