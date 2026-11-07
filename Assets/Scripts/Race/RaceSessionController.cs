using UnityEngine;

namespace SkiRace
{
    /// <summary>
    /// 一局结束后的会话控制：再来一局 / 返回选择。
    /// 联机接入时，把 Rematch / ReturnToSelection 内部换成网络侧的重置与房间流程，
    /// 不要使用 SceneManager.LoadScene。
    /// </summary>
    public sealed class RaceSessionController : MonoBehaviour
    {
        public RaceDirector director;
        public LocalRaceSimulator simulator;
        public RaceResultPanel resultPanel;
        public GearSelect gearSelection;
        public bool deactivateBoardsOnReturn = true;

        private void Start()
        {
            Resolve();
        }

        private void Resolve()
        {
            if (director == null) director = RaceDirector.Instance;
            if (simulator == null) simulator = FindObjectOfType<LocalRaceSimulator>(true);
            if (resultPanel == null) resultPanel = FindObjectOfType<RaceResultPanel>(true);
            if (gearSelection == null) gearSelection = FindObjectOfType<GearSelect>(true);
        }

        /// <summary>再来一局：不重载场景，清成绩、回到起跑槽、重新倒计时。</summary>
        public void Rematch()
        {
            Resolve();
            if (resultPanel != null) resultPanel.Hide();

            if (simulator != null)
            {
                simulator.StartDemo();
                return;
            }

            if (director != null) director.StartCountdown();
        }

        /// <summary>返回选择：结束当前演示并恢复装备选择页。联机时这里要改成网络房间流程。</summary>
        public void ReturnToSelection()
        {
            Resolve();
            if (resultPanel != null) resultPanel.Hide();

            if (simulator != null)
            {
                simulator.StopDemo();
            }
            else if (director != null)
            {
                director.AbortToLobby();
                director.ClearRacers();
            }

            if (gearSelection == null) return;

            if (deactivateBoardsOnReturn)
            {
                if (gearSelection.boardSingle != null) gearSelection.boardSingle.SetActive(false);
                if (gearSelection.boardDouble != null) gearSelection.boardDouble.SetActive(false);
            }

            if (gearSelection.selectUIPanel != null)
                gearSelection.selectUIPanel.SetActive(true);
        }
    }
}
