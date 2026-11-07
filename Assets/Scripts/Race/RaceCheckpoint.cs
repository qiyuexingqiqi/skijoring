using UnityEngine;

namespace SkiRace
{
    /// <summary>
    /// 赛道检查点。最后一个检查点同时作为终点。
    /// 触发器只负责上报，是否有效、是否按顺序、是否作弊全部由 RaceDirector 判定。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class RaceCheckpoint : MonoBehaviour
    {
        public int index;
        public bool isFinish;
        [Tooltip("虚拟选手用它判断是否到达（真实玩家走 Trigger）")]
        public float reachRadius = 2f;

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(60f, 24f, 6f);
            box.center = new Vector3(0f, 10f, 0f);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (RaceDirector.Instance == null) return;
            var identity = other.GetComponentInParent<RaceRacerIdentity>();
            if (identity == null) return;
            RaceDirector.Instance.ReportCheckpoint(identity.racerId, index, isFinish);
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = isFinish
                ? new Color(0.59f, 0.92f, 0.79f, 0.55f)
                : new Color(0.48f, 0.84f, 1f, 0.45f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = isFinish
                ? new Color(0.59f, 0.92f, 0.79f, 0.95f)
                : new Color(0.48f, 0.84f, 1f, 0.95f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
