using UnityEngine;

public class ObjectPlacementHolder : MonoBehaviour
{
    [Header("摆放预制体列表（护栏，道具）")]
    public GameObject[] prefabList;

    [Header("当前选择预制索引")]
    public int currentPrefabIndex = 0;

    [Header("只在该层级上摆放(地面层)")]
    public LayerMask groundLayer;

    [Header("预览透明度")]
    [Range(0, 1)] public float previewAlpha = 0.5f;
}

