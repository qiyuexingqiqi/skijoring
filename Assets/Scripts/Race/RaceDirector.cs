using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkiRace
{
    /// <summary>
    /// 比赛规则核心：状态机、检查点校验、进度/排名、终点判定、复位与边界规则。
    /// 不依赖任何网络库；联机时由适配器把玩家 id、位置和检查点上报喂进来。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class RaceDirector : MonoBehaviour
    {
        public RaceTrackDefinition track;

        [Header("规则")]
        [Tooltip("倒计时秒数")]
        public float countdownSeconds = 3f;
        [Tooltip("排名刷新间隔（秒），对应需求里的 0.2s")]
        public float rankingInterval = 0.2f;
        [Tooltip("比赛超时，超时未完成按未完赛处理")]
        public float raceTimeLimit = 240f;
        [Tooltip("两个检查点之间的最小间隔，防止瞬移刷点")]
        public float minCheckpointInterval = 0.35f;
        [Tooltip("检查点之间的最大允许速度（m/s），超过视为作弊")]
        public float maxCheckpointSpeed = 80f;
        [Tooltip("R 复位的罚时（秒）")]
        public float resetPenaltySeconds = 3f;
        [Tooltip("严格按顺序通过检查点")]
        public bool strictCheckpointOrder = true;
        [Tooltip("进入 Play 后自动倒计时，便于离线演示")]
        public bool autoStart = false;

        /// <summary>时间源。联机时由适配器替换成统一网络时间；离线默认 Time.timeAsDouble。</summary>
        [NonSerialized] public Func<double> clock;

        public static RaceDirector Instance { get; private set; }

        public RacePhase Phase { get; private set; } = RacePhase.Lobby;
        public IReadOnlyList<RaceRankRow> Ranking => ranking;
        public IReadOnlyList<RaceRankRow> Results => results;
        public int RacerCount => racers.Count;
        public bool IsRacing => Phase == RacePhase.Racing;

        public double CurrentRaceTime =>
            (Phase == RacePhase.Racing || Phase == RacePhase.Finished) && startTime > 0
                ? Math.Max(0d, CurrentTime - startTime)
                : 0d;

        public float CountdownRemaining =>
            Phase == RacePhase.Countdown ? Mathf.Max(0f, (float)(countdownEnd - CurrentTime)) : 0f;

        public event Action<RacePhase> PhaseChanged;
        public event Action<IReadOnlyList<RaceRankRow>> RankingChanged;
        public event Action<int> RacerFinished;
        public event Action<IReadOnlyList<RaceRankRow>> RaceCompleted;
        public event Action<int, Vector3> RacerResetRequested;

        private sealed class RacerState
        {
            public int id;
            public string name;
            public bool isLocal;
            public RacerStatus status;
            public int lastCheckpoint = -1;
            public double lastCheckpointTime;
            public double finishTime;
            public double penalty;
            public double finalElapsed;
            public Vector3 position;
            public bool hasPosition;
            public double positionTime;
            public float progress;
            public float distanceToNext;
        }

        private readonly Dictionary<int, RacerState> racers = new Dictionary<int, RacerState>();
        private readonly List<RaceRankRow> ranking = new List<RaceRankRow>();
        private readonly List<RaceRankRow> results = new List<RaceRankRow>();
        private readonly List<RacerState> sortBuffer = new List<RacerState>();

        private double startTime;
        private double countdownEnd;
        private double nextRankingTime;
        private bool completed;

        private double CurrentTime => clock != null ? clock() : Time.timeAsDouble;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (autoStart) StartCountdown();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            Tick(CurrentTime);
        }

        // ============ 注册与数据输入 ============

        public void RegisterRacer(int id, string displayName, bool isLocal = false)
        {
            RacerState state;
            if (racers.TryGetValue(id, out state))
            {
                state.name = displayName;
                state.isLocal = isLocal;
                return;
            }

            state = new RacerState
            {
                id = id,
                name = string.IsNullOrEmpty(displayName) ? "Racer " + id : displayName,
                isLocal = isLocal,
                status = RacerStatus.Waiting
            };

            if (Phase == RacePhase.Racing)
            {
                state.status = RacerStatus.Racing;
                state.lastCheckpointTime = CurrentTime;
            }
            racers[id] = state;

            if (Phase == RacePhase.Racing) RecalculateRanking(CurrentTime);
        }

        public void UnregisterRacer(int id)
        {
            RacerState state;
            if (!racers.TryGetValue(id, out state)) return;
            if (Phase == RacePhase.Racing && state.status == RacerStatus.Racing)
            {
                state.status = RacerStatus.Disconnected;
                state.finalElapsed = Math.Max(0d, CurrentTime - startTime - state.penalty);
                if (AllDecided()) CompleteRace(CurrentTime);
            }
            else
            {
                racers.Remove(id);
            }
            RecalculateRanking(CurrentTime);
        }

        public void ClearRacers()
        {
            racers.Clear();
            ranking.Clear();
            results.Clear();
            sortBuffer.Clear();
        }

        /// <summary>适配器每帧或按同步频率写入玩家位置；离线由假对手写入。</summary>
        public void SetRacerPosition(int id, Vector3 position, double time = -1d)
        {
            RacerState state;
            if (!racers.TryGetValue(id, out state)) return;
            state.position = position;
            state.positionTime = time >= 0d ? time : CurrentTime;
            state.hasPosition = true;
        }

        public RacerStatus GetRacerStatus(int id)
        {
            RacerState state;
            return racers.TryGetValue(id, out state) ? state.status : RacerStatus.Waiting;
        }

        public int GetLastCheckpointIndex(int id)
        {
            RacerState state;
            return racers.TryGetValue(id, out state) ? state.lastCheckpoint : -1;
        }

        // ============ 流程 ============

        public void StartCountdown()
        {
            completed = false;
            results.Clear();
            startTime = 0d;
            countdownEnd = CurrentTime + Mathf.Max(0f, countdownSeconds);
            Phase = RacePhase.Countdown;

            foreach (var state in racers.Values)
            {
                if (state.status == RacerStatus.Disconnected) continue;
                state.status = RacerStatus.Waiting;
                state.lastCheckpoint = -1;
                state.finishTime = 0d;
                state.penalty = 0d;
                state.finalElapsed = 0d;
                state.progress = 0f;
                state.distanceToNext = 0f;
            }

            RecalculateRanking(CurrentTime);
            PhaseChanged?.Invoke(Phase);
        }

        public void Tick(double now)
        {
            if (Phase == RacePhase.Countdown && now >= countdownEnd)
                StartRaceInternal(now);

            if (Phase != RacePhase.Racing) return;

            if (raceTimeLimit > 0f && now - startTime >= raceTimeLimit)
            {
                CompleteRace(now);
                return;
            }

            if (AllDecided())
            {
                CompleteRace(now);
                return;
            }

            if (now >= nextRankingTime)
            {
                nextRankingTime = now + Mathf.Max(0.05f, rankingInterval);
                RecalculateRanking(now);
            }
        }

        private void StartRaceInternal(double now)
        {
            Phase = RacePhase.Racing;
            startTime = now;
            completed = false;
            results.Clear();

            foreach (var state in racers.Values)
            {
                if (state.status == RacerStatus.Disconnected) continue;
                state.status = RacerStatus.Racing;
                state.lastCheckpoint = -1;
                state.lastCheckpointTime = now;
                state.finishTime = 0d;
                state.penalty = 0d;
                state.finalElapsed = 0d;
                state.progress = 0f;
                state.distanceToNext = 0f;
            }

            nextRankingTime = now;
            RecalculateRanking(now);
            PhaseChanged?.Invoke(Phase);
        }

        public void ForceFinishRace()
        {
            CompleteRace(CurrentTime);
        }

        public void AbortToLobby()
        {
            Phase = RacePhase.Lobby;
            completed = false;
            startTime = 0d;
            countdownEnd = 0d;
            results.Clear();
            ranking.Clear();

            foreach (var state in racers.Values)
            {
                state.status = RacerStatus.Waiting;
                state.lastCheckpoint = -1;
                state.finishTime = 0d;
                state.penalty = 0d;
                state.finalElapsed = 0d;
                state.progress = 0f;
                state.distanceToNext = 0f;
            }

            PhaseChanged?.Invoke(Phase);
        }

        // ============ 检查点与终点 ============

        /// <summary>检查点触发器/适配器统一调用这里。是否有效全部在权威端判定。</summary>
        public void ReportCheckpoint(int id, int checkpointIndex, bool isFinish)
        {
            RacerState state;
            if (!racers.TryGetValue(id, out state)) return;
            if (Phase != RacePhase.Racing || state.status != RacerStatus.Racing) return;
            if (track == null || track.CheckpointCount == 0) return;

            double now = CurrentTime;
            int finishIndex = track.FinishIndex;
            bool finish = isFinish || checkpointIndex == finishIndex;

            if (finish)
            {
                // 必须已经通过终点前的所有检查点
                if (state.lastCheckpoint < finishIndex - 1) return;

                // 终点也做一次速度合理性检查，防止瞬移到终点
                if (state.lastCheckpoint >= 0 && now > state.lastCheckpointTime)
                {
                    Vector3 previous = track.GetCheckpointPosition(state.lastCheckpoint);
                    Vector3 finishPosition = track.GetCheckpointPosition(finishIndex);
                    double dt = now - state.lastCheckpointTime;
                    if (dt > 0.001d && Vector3.Distance(previous, finishPosition) / dt > maxCheckpointSpeed)
                        return;
                }

                state.lastCheckpoint = finishIndex;
                state.lastCheckpointTime = now;
                state.status = RacerStatus.Finished;
                state.finishTime = Math.Max(0d, now - startTime - state.penalty);
                state.progress = 1f;
                state.distanceToNext = 0f;

                RacerFinished?.Invoke(id);
                RecalculateRanking(now);
                if (AllDecided()) CompleteRace(now);
                return;
            }

            // 重复/倒序上报直接忽略
            if (checkpointIndex <= state.lastCheckpoint) return;

            if (strictCheckpointOrder && checkpointIndex != state.lastCheckpoint + 1)
            {
                Debug.LogWarning($"[Race] 忽略跳点上报: racer={id} cp={checkpointIndex} last={state.lastCheckpoint}");
                return;
            }

            if (state.lastCheckpoint >= 0 && now - state.lastCheckpointTime < minCheckpointInterval)
                return;

            if (state.lastCheckpoint >= 0 && now > state.lastCheckpointTime)
            {
                Vector3 previous = track.GetCheckpointPosition(state.lastCheckpoint);
                Vector3 current = track.GetCheckpointPosition(checkpointIndex);
                double dt = now - state.lastCheckpointTime;
                if (dt > 0.001d && Vector3.Distance(previous, current) / dt > maxCheckpointSpeed)
                {
                    Debug.LogWarning($"[Race] 忽略疑似作弊的检查点: racer={id} cp={checkpointIndex}");
                    return;
                }
            }

            state.lastCheckpoint = checkpointIndex;
            state.lastCheckpointTime = now;
            RecalculateRanking(now);
        }

        /// <summary>R 复位：回到最近检查点并加罚时。真正的传送由适配器/假对手执行。</summary>
        public void ResetRacer(int id)
        {
            RacerState state;
            if (!racers.TryGetValue(id, out state)) return;
            if (Phase != RacePhase.Racing || state.status != RacerStatus.Racing) return;

            double now = CurrentTime;
            state.penalty += resetPenaltySeconds;
            state.lastCheckpointTime = now;
            state.distanceToNext = 0f;

            Vector3 fallback = state.hasPosition ? state.position : (transform != null ? transform.position : Vector3.zero);
            Vector3 target = track != null
                ? track.GetRespawnPosition(state.lastCheckpoint, fallback)
                : fallback;

            state.position = target;
            state.hasPosition = true;

            RacerResetRequested?.Invoke(id, target);
            RecalculateRanking(now);
        }

        // ============ 排名与结算 ============

        private void RecalculateRanking(double now)
        {
            if (track == null || track.CheckpointCount == 0) return;

            sortBuffer.Clear();
            foreach (var state in racers.Values)
            {
                ComputeProgress(state);
                sortBuffer.Add(state);
            }

            sortBuffer.Sort((a, b) => CompareRacers(a, b));

            ranking.Clear();
            int rank = 1;
            for (int i = 0; i < sortBuffer.Count; i++)
            {
                var state = sortBuffer[i];
                if (i > 0 && !IsTie(sortBuffer[i - 1], state)) rank = i + 1;

                ranking.Add(new RaceRankRow
                {
                    racerId = state.id,
                    displayName = state.name,
                    isLocal = state.isLocal,
                    rank = rank,
                    checkpointIndex = state.lastCheckpoint,
                    progress = state.progress,
                    elapsedSeconds = ElapsedFor(state, now),
                    status = state.status
                });
            }

            RankingChanged?.Invoke(ranking);
        }

        private void ComputeProgress(RacerState state)
        {
            int count = track.CheckpointCount;

            if (state.status == RacerStatus.Finished)
            {
                state.progress = 1f;
                state.distanceToNext = 0f;
                return;
            }

            if (state.status == RacerStatus.Waiting)
            {
                state.progress = 0f;
                state.distanceToNext = 9999f;
                return;
            }

            int next = state.lastCheckpoint + 1;
            if (next >= count)
            {
                state.progress = 1f;
                state.distanceToNext = 0f;
                return;
            }

            Vector3 target = track.GetCheckpointPosition(next);
            Vector3 previous = state.lastCheckpoint >= 0
                ? track.GetCheckpointPosition(state.lastCheckpoint)
                : (track.GetStartSlot(0) != null ? track.GetStartSlot(0).position : state.position);

            float distance = Vector3.Distance(state.position, target);
            float segment = Mathf.Max(1f, Vector3.Distance(previous, target));
            float fraction = Mathf.Clamp01(1f - distance / segment);

            state.distanceToNext = distance;
            state.progress = Mathf.Clamp01((state.lastCheckpoint + 1 + fraction) / Mathf.Max(1, count));
        }

        private static int StatusPriority(RacerStatus status)
        {
            switch (status)
            {
                case RacerStatus.Finished: return 0;
                case RacerStatus.Racing: return 1;
                case RacerStatus.Dnf: return 2;
                case RacerStatus.Disconnected: return 3;
                default: return 4;
            }
        }

        private static int CompareRacers(RacerState a, RacerState b)
        {
            int priority = StatusPriority(a.status) - StatusPriority(b.status);
            if (priority != 0) return priority;

            if (a.status == RacerStatus.Finished && b.status == RacerStatus.Finished)
            {
                if (Math.Abs(a.finishTime - b.finishTime) > 0.0005d)
                    return a.finishTime < b.finishTime ? -1 : 1;
                return a.id.CompareTo(b.id);
            }

            if (a.lastCheckpoint != b.lastCheckpoint)
                return b.lastCheckpoint - a.lastCheckpoint;

            if (Math.Abs(a.distanceToNext - b.distanceToNext) > 0.01f)
                return a.distanceToNext < b.distanceToNext ? -1 : 1;

            return a.id.CompareTo(b.id);
        }

        private static bool IsTie(RacerState a, RacerState b)
        {
            if (a.status != RacerStatus.Finished || b.status != RacerStatus.Finished) return false;
            return Math.Abs(a.finishTime - b.finishTime) <= 0.005d;
        }

        private double ElapsedFor(RacerState state, double now)
        {
            if (state.status == RacerStatus.Finished) return state.finishTime;
            if (state.status == RacerStatus.Dnf || state.status == RacerStatus.Disconnected) return state.finalElapsed;
            if (state.status == RacerStatus.Racing) return Math.Max(0d, now - startTime - state.penalty);
            return 0d;
        }

        private bool AllDecided()
        {
            if (racers.Count == 0) return false;
            foreach (var state in racers.Values)
            {
                if (state.status == RacerStatus.Racing || state.status == RacerStatus.Waiting)
                    return false;
            }
            return true;
        }

        private void CompleteRace(double now)
        {
            if (completed) return;
            completed = true;

            foreach (var state in racers.Values)
            {
                if (state.status == RacerStatus.Racing || state.status == RacerStatus.Waiting)
                {
                    state.status = RacerStatus.Dnf;
                    state.finalElapsed = Math.Max(0d, now - startTime - state.penalty);
                }
                else if (state.status == RacerStatus.Disconnected)
                {
                    state.finalElapsed = Math.Max(0d, now - startTime - state.penalty);
                }
            }

            RecalculateRanking(now);
            results.Clear();
            results.AddRange(ranking);
            Phase = RacePhase.Finished;

            PhaseChanged?.Invoke(Phase);
            RaceCompleted?.Invoke(results);
        }
    }
}
