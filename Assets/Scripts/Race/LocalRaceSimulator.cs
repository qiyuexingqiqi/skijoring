using System.Collections.Generic;
using UnityEngine;

namespace SkiRace
{
    /// <summary>
    /// 离线假对手：生成 7 个虚拟选手，加上可选的本地球员，凑成 8 人。
    /// 用来在没有任何网络代码的情况下验证排名、完赛、同分、未完赛和复位。
    /// 联机时把这里替换成网络适配器即可，RaceDirector 不需要改。
    /// </summary>
    public sealed class LocalRaceSimulator : MonoBehaviour
    {
        public RaceDirector director;
        public RaceTrackDefinition track;
        public int ghostCount = 7;
        public bool includeLocalPlayer = true;
        public bool teleportLocalToStartSlot = true;
        public float baseGhostSpeed = 20f;
        public float ghostSpeedStep = 2.5f;

        public bool Running { get; private set; }

        private sealed class Ghost
        {
            public GameObject gameObject;
            public RaceRacerIdentity identity;
            public int waypoint;
            public float speed;
            public bool finished;
        }

        private readonly List<Ghost> ghosts = new List<Ghost>();
        private readonly List<Vector3> path = new List<Vector3>();
        private GameObject extraRoot;

        private RaceRacerIdentity localIdentity;
        private Rigidbody localBody;
        private int localId = -1;
        private bool addedLocalIdentity;

        private void Start()
        {
            if (director == null) director = RaceDirector.Instance;
            if (director != null) director.RacerResetRequested += OnResetRequested;
        }

        private void OnDestroy()
        {
            if (director != null) director.RacerResetRequested -= OnResetRequested;
            DestroyGhosts();
            RemoveLocalIdentity();
        }

        public void StartDemo()
        {
            if (director == null) director = RaceDirector.Instance;
            if (director == null || track == null || track.CheckpointCount == 0)
            {
                Debug.LogWarning("[RaceDemo] 缺少 RaceDirector 或赛道数据，请先运行 Tools/Race/Setup Offline Race Test");
                return;
            }

            StopDemo();
            BuildPath();

            director.ClearRacers();
            director.AbortToLobby();

            if (includeLocalPlayer) TryBindLocalPlayer();

            if (localId >= 0)
            {
                Vector3 localStart = localBody != null
                    ? localBody.position
                    : (track.GetStartSlot(0) != null ? track.GetStartSlot(0).position : Vector3.zero);

                if (teleportLocalToStartSlot && localBody != null)
                {
                    Transform slot = track.GetStartSlot(0);
                    if (slot != null)
                    {
                        localBody.position = slot.position + Vector3.up * 0.5f;
                        localBody.velocity = Vector3.zero;
                        localBody.angularVelocity = Vector3.zero;
                        localStart = localBody.position;
                    }
                }

                director.SetRacerPosition(localId, localStart);
            }

            extraRoot = new GameObject("RaceGhosts");

            // 有本地玩家时补 7 个假对手；没有本地玩家时补足 8 个假对手
            int spawnCount = ghostCount + (localId >= 0 ? 0 : 1);
            for (int i = 0; i < spawnCount; i++)
            {
                SpawnGhost(i);
            }

            Running = true;
            director.StartCountdown();
        }

        public void StopDemo()
        {
            Running = false;
            DestroyGhosts();
            RemoveLocalIdentity();
            path.Clear();

            if (director != null)
            {
                director.AbortToLobby();
                director.ClearRacers();
            }
        }

        private void BuildPath()
        {
            path.Clear();
            var start = track.GetStartSlot(0);
            if (start != null)
            {
                path.Add(start.position);
            }
            else
            {
                // 没有起跑槽位时，用第一个检查点后方 20m 作为虚拟起点
                Vector3 first = track.GetCheckpointPosition(0);
                Vector3 forward = track.CheckpointCount > 1
                    ? (track.GetCheckpointPosition(1) - first).normalized
                    : Vector3.forward;
                path.Add(first - forward * 20f);
            }

            for (int i = 0; i < track.CheckpointCount; i++)
                path.Add(track.GetCheckpointPosition(i));
        }

        private void TryBindLocalPlayer()
        {
            CarvingController carving = null;
            foreach (var candidate in FindObjectsOfType<CarvingController>(true))
            {
                if (candidate != null && candidate.gameObject.activeInHierarchy) { carving = candidate; break; }
            }

            Snowboard.PlayerController single = null;
            if (carving == null)
            {
                foreach (var candidate in FindObjectsOfType<Snowboard.PlayerController>(true))
                {
                    if (candidate != null && candidate.gameObject.activeInHierarchy) { single = candidate; break; }
                }
            }

            GameObject player = carving != null ? carving.gameObject : (single != null ? single.gameObject : null);
            if (player == null) return;

            localBody = player.GetComponent<Rigidbody>();
            if (localBody == null) localBody = player.GetComponentInChildren<Rigidbody>();

            localIdentity = player.GetComponent<RaceRacerIdentity>();
            if (localIdentity == null)
            {
                localIdentity = player.AddComponent<RaceRacerIdentity>();
                addedLocalIdentity = true;
            }

            localId = 1;
            localIdentity.racerId = localId;
            localIdentity.displayName = "本地玩家";
            localIdentity.isLocalPlayer = true;

            director.RegisterRacer(localId, localIdentity.displayName, true);
        }

        private void RemoveLocalIdentity()
        {
            if (localIdentity != null && addedLocalIdentity)
            {
                Destroy(localIdentity);
            }
            localIdentity = null;
            localBody = null;
            localId = -1;
            addedLocalIdentity = false;
        }

        private void SpawnGhost(int index)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Ghost_" + (index + 1);
            go.transform.SetParent(extraRoot.transform, false);

            var collider = go.GetComponent<CapsuleCollider>();
            if (collider != null) collider.isTrigger = true;

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var identity = go.AddComponent<RaceRacerIdentity>();
            identity.racerId = 100 + index;
            identity.displayName = "虚拟选手 " + (index + 1);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.HSVToRGB((index + 3) / (float)Mathf.Max(1, ghostCount), 0.55f, 0.95f);
            }

            Vector3 start;
            int slotIndex = localId >= 0 ? index + 1 : index;
            Transform slot = track.GetStartSlot(slotIndex);
            if (slot != null)
                start = slot.position;
            else
                start = path.Count > 0 ? path[0] : Vector3.zero;
            go.transform.position = start + Vector3.up * 1.2f;

            var ghost = new Ghost
            {
                gameObject = go,
                identity = identity,
                waypoint = path.Count > 1 ? 1 : 0,
                speed = baseGhostSpeed + index * ghostSpeedStep,
                finished = false
            };

            ghosts.Add(ghost);
            director.RegisterRacer(identity.racerId, identity.displayName, false);
            director.SetRacerPosition(identity.racerId, go.transform.position);
        }

        private void Update()
        {
            if (!Running || director == null) return;

            if (Input.GetKeyDown(KeyCode.R))
            {
                if (localId >= 0) director.ResetRacer(localId);
                else
                {
                    for (int i = 0; i < ghosts.Count; i++) director.ResetRacer(ghosts[i].identity.racerId);
                }
            }

            if (director.Phase != RacePhase.Racing) return;

            double now = director.clock != null ? director.clock() : Time.timeAsDouble;

            if (localId >= 0)
            {
                if (localBody != null && localBody.gameObject.activeInHierarchy)
                    director.SetRacerPosition(localId, localBody.position, now);
            }

            for (int i = 0; i < ghosts.Count; i++)
            {
                var ghost = ghosts[i];
                if (ghost.gameObject == null || ghost.finished) continue;

                if (ghost.waypoint >= path.Count)
                {
                    ghost.finished = true;
                    continue;
                }

                Vector3 target = path[ghost.waypoint] + Vector3.up * 1.2f;
                ghost.gameObject.transform.position = Vector3.MoveTowards(
                    ghost.gameObject.transform.position,
                    target,
                    ghost.speed * Time.deltaTime);

                Vector3 flatDirection = target - ghost.gameObject.transform.position;
                flatDirection.y = 0f;
                if (flatDirection.sqrMagnitude > 0.001f)
                    ghost.gameObject.transform.rotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);

                director.SetRacerPosition(ghost.identity.racerId, ghost.gameObject.transform.position, now);

                int checkpointIndex = ghost.waypoint - 1;
                float reachRadius = track.GetCheckpointRadius(checkpointIndex);
                if (Vector3.Distance(ghost.gameObject.transform.position, target) <= Mathf.Max(0.5f, reachRadius))
                {
                    bool isFinish = checkpointIndex == track.FinishIndex;
                    director.ReportCheckpoint(ghost.identity.racerId, checkpointIndex, isFinish);
                    ghost.waypoint++;
                    if (isFinish) ghost.finished = true;
                }
            }
        }

        private void OnResetRequested(int racerId, Vector3 target)
        {
            if (localId == racerId && localBody != null)
            {
                localBody.position = target;
                localBody.velocity = Vector3.zero;
                localBody.angularVelocity = Vector3.zero;
                return;
            }

            for (int i = 0; i < ghosts.Count; i++)
            {
                var ghost = ghosts[i];
                if (ghost.identity == null || ghost.identity.racerId != racerId) continue;
                ghost.gameObject.transform.position = target;
                ghost.finished = false;
                int lastCheckpoint = director != null ? director.GetLastCheckpointIndex(racerId) : -1;
                ghost.waypoint = Mathf.Clamp(lastCheckpoint + 1, 0, path.Count - 1);
                break;
            }
        }

        private void DestroyGhosts()
        {
            for (int i = 0; i < ghosts.Count; i++)
            {
                if (ghosts[i].gameObject != null) Destroy(ghosts[i].gameObject);
            }
            ghosts.Clear();

            if (extraRoot != null)
            {
                Destroy(extraRoot);
                extraRoot = null;
            }
        }
    }
}
