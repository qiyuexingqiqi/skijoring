using UnityEngine;
using UnityEngine.UI;
using TMPro; // 引入 TextMeshPro 的命名空间，因为你之前导入了 TMP

public class BoardSelector : MonoBehaviour
{
    // 1. 定义一个变量来接收屏幕上的文字组件
    public TextMeshProUGUI selectionText;

    public int currentBoardType = 0;

    // 单板按钮点击事件
    public void SelectSingleBoard()
    {
        currentBoardType = 0;
        Debug.Log("已选择单板");

        // 2. 加上这一行：改变屏幕上的文字
        if (selectionText != null)
            selectionText.text = "当前选择：单板";
    }

    // 双板按钮点击事件
    public void SelectDoubleBoard()
    {
        currentBoardType = 1;
        Debug.Log("已选择双板");

        // 2. 加上这一行：改变屏幕上的文字
        if (selectionText != null)
            selectionText.text = "当前选择：双板";
    }
}