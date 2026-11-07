using System;

namespace SkiRace
{
    /// <summary>比赛阶段。联机接入时由权威端驱动同一套状态。</summary>
    public enum RacePhase
    {
        Lobby,
        Countdown,
        Racing,
        Finished
    }

    /// <summary>单个选手的比赛状态。</summary>
    public enum RacerStatus
    {
        Waiting,
        Racing,
        Finished,
        Dnf,
        Disconnected
    }

    /// <summary>排行榜/结算面板共用的一行数据，网络层以后直接填同一结构。</summary>
    [Serializable]
    public struct RaceRankRow
    {
        public int racerId;
        public string displayName;
        public bool isLocal;
        public int rank;
        public int checkpointIndex;
        public float progress;
        public double elapsedSeconds;
        public RacerStatus status;
    }
}
