using System;
using UnityEngine;

namespace SkiRace
{
    /// <summary>
    /// 赛道数据：按顺序排列的检查点（最后一个是终点）和起跑槽位。
    /// 由编辑器工具沿主路线自动布点，也可以手动在 Inspector 里调整。
    /// </summary>
    public sealed class RaceTrackDefinition : MonoBehaviour
    {
        public RaceCheckpoint[] checkpoints = new RaceCheckpoint[0];
        public Transform[] startSlots = new Transform[0];

        public int CheckpointCount => checkpoints != null ? checkpoints.Length : 0;
        public int FinishIndex => CheckpointCount - 1;

        public RaceCheckpoint GetCheckpoint(int index)
        {
            if (checkpoints == null || index < 0 || index >= checkpoints.Length) return null;
            return checkpoints[index];
        }

        public Vector3 GetCheckpointPosition(int index)
        {
            var checkpoint = GetCheckpoint(index);
            return checkpoint != null ? checkpoint.transform.position : transform.position;
        }

        public float GetCheckpointRadius(int index)
        {
            var checkpoint = GetCheckpoint(index);
            return checkpoint != null ? Mathf.Max(0.5f, checkpoint.reachRadius) : 1f;
        }

        public Transform GetStartSlot(int index)
        {
            if (startSlots == null || startSlots.Length == 0) return null;
            return startSlots[Mathf.Clamp(index, 0, startSlots.Length - 1)];
        }

        /// <summary>按实际参赛人数居中的起跑位置，2 人比赛也能站在雪道正中间。</summary>
        public Vector3 GetStartPosition(int index, int count)
        {
            count = Mathf.Max(1, count);
            Vector3 anchor = GetCheckpointPosition(0);
            Vector3 forward = Vector3.ProjectOnPlane(GetCheckpointPosition(1) - anchor, Vector3.up);
            forward = forward.sqrMagnitude > .0001f ? forward.normalized : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 position = anchor - forward * 6f + right * ((index - (count - 1) * .5f) * 2.5f);

            // CP0 在坡道下方，直接沿用它的高度会让起跑点陷进雪里；改为向下采样最近的实际雪面。
            RaycastHit closest = default;
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 25f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                float delta = Mathf.Abs(hit.point.y - anchor.y);
                if (delta < best) { best = delta; closest = hit; }
            }
            if (closest.collider != null) position.y = closest.point.y;
            return position;
        }

        /// <summary>复位点：回到最近通过的检查点；一个都没过就回起点。</summary>
        public Vector3 GetRespawnPosition(int lastCheckpointIndex, Vector3 fallback)
        {
            if (lastCheckpointIndex >= 0 && lastCheckpointIndex < CheckpointCount)
                return GetCheckpointPosition(lastCheckpointIndex) + Vector3.up * 1.5f;

            var slot = GetStartSlot(0);
            if (slot != null) return slot.position + Vector3.up * 0.5f;
            return fallback;
        }

        /// <summary>从子物体重新收集检查点，按 index 排序。</summary>
        public void CollectFromChildren()
        {
            var found = GetComponentsInChildren<RaceCheckpoint>(true);
            Array.Sort(found, (a, b) => a.index.CompareTo(b.index));
            checkpoints = found;
        }

        private void OnDrawGizmos()
        {
            if (checkpoints == null) return;
            for (int i = 0; i < checkpoints.Length; i++)
            {
                if (checkpoints[i] == null) continue;
                Gizmos.color = checkpoints[i].isFinish
                    ? new Color(0.59f, 0.92f, 0.79f, 1f)
                    : new Color(0.48f, 0.84f, 1f, 1f);
                Gizmos.DrawWireSphere(checkpoints[i].transform.position, 1.2f);
                if (i > 0 && checkpoints[i - 1] != null)
                {
                    Gizmos.color = new Color(0.48f, 0.84f, 1f, 0.25f);
                    Gizmos.DrawLine(checkpoints[i - 1].transform.position, checkpoints[i].transform.position);
                }
            }

            if (startSlots == null) return;
            Gizmos.color = new Color(0.59f, 0.92f, 0.79f, 0.9f);
            foreach (var slot in startSlots)
            {
                if (slot == null) continue;
                Gizmos.DrawWireCube(slot.position, new Vector3(1.6f, 0.4f, 2.4f));
            }
        }
    }
}
