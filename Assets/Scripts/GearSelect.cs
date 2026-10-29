using UnityEngine;

public class GearSelect : MonoBehaviour
{
    [Header("滑雪人物")]
    public GameObject boardSingle;
    public GameObject boardDouble;
    [Header("装备选择界面")]
    public GameObject selectUIPanel;
    public SkiRaceManager raceManager;

    public void OnClickSingle() { Select(false); }
    public void OnClickDouble() { Select(true); }

    private void Select(bool doubleBoard)
    {
        if (raceManager != null && raceManager.IsFinished) return;
        boardSingle.SetActive(!doubleBoard);
        boardDouble.SetActive(doubleBoard);
        selectUIPanel.SetActive(false);
        if (raceManager != null) raceManager.BeginRun(doubleBoard ? boardDouble : boardSingle, doubleBoard);
    }
}
