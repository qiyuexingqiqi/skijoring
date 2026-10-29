using UnityEngine;

namespace Snowboard
{
    /// <summary>Layers turn balance over the evaluated riding animation without moving the board or feet.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class SnowboardUpperBodyBalance : MonoBehaviour
    {
        [Header("Upper Body Bones (automatically resolved when empty)")]
        [SerializeField] private Transform spine;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform head;

        [Header("Turn Balance")]
        [Tooltip("Shoulder counter-rotation relative to the turning board, in degrees.")]
        [SerializeField, Range(0f, 40f)] private float counterRotation = 22f;
        [SerializeField, Range(0f, 20f)] private float turnLean = 10f;
        [SerializeField, Range(0f, 10f)] private float turnCrouch = 3f;
        [Tooltip("Board turn rate in degrees/second that produces the full pose.")]
        [SerializeField, Min(1f)] private float fullPoseTurnRate = 60f;
        [SerializeField, Min(0.51f)] private float fullPoseSpeed = 8f;
        [SerializeField, Min(0.01f)] private float responseTime = 0.16f;
        [SerializeField, Min(0.01f)] private float returnTime = 0.24f;

        private PlayerController player;
        private Animator animator;
        private float turn;
        private float turnVelocity;
        private Quaternion spineAnimation;
        private Quaternion chestAnimation;
        private Quaternion headAnimation;
        private bool poseApplied;

        public void Initialize(PlayerController controller, Animator modelAnimator)
        {
            RestoreAnimationPose();
            player = controller;
            animator = modelAnimator;
            turn = turnVelocity = 0f;

            if (animator != null && animator.isHuman)
            {
                if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Spine);
                if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                if (head == null) head = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            // Also support the bundled deform-bone names when no Humanoid mapping is available.
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (spine == null && bone.name == "DEF-spine") spine = bone;
                if (chest == null && bone.name == "DEF-chest") chest = bone;
                if (head == null && bone.name == "DEF-head") head = bone;
            }

            if (spine == null)
            {
                Debug.LogWarning("[SnowboardUpperBodyBalance] Assign an upper body spine bone on " + name, this);
                enabled = false;
            }
        }

        private void Update()
        {
            // Remove only our previous offset before model positioning and Animator evaluation.
            // This also prevents accumulated twists when the Animator is culled or paused.
            RestoreAnimationPose();
        }

        private void LateUpdate()
        {
            if (player == null || animator == null || !animator.isActiveAndEnabled || spine == null)
                return;
            if (player.State == PlayerState.Dead || Time.deltaTime <= 0f)
                return;

            int pose = animator.GetInteger("pose");
            bool riding = pose == (int)EAnim.ride || pose == (int)EAnim.bomb || pose == (int)EAnim.shaky;
            bool canBalance = player.State == PlayerState.Ground && riding
                && player.GrabState == PlayerGrabState.NO_GRAB && !player.IsLanding;

            // Use the actual control rotation, so all input devices and braking obey the same rules.
            float turnRate = player.FrameRotY / Time.deltaTime;
            float speed = Vector3.ProjectOnPlane(player.Velocity, player.BordUpVector).magnitude;
            float speedWeight = Mathf.InverseLerp(0.5f, fullPoseSpeed, speed);
            float target = canBalance ? Mathf.Clamp(turnRate / fullPoseTurnRate, -1f, 1f) * speedWeight : 0f;
            float smoothTime = Mathf.Abs(target) > 0.001f ? responseTime : returnTime;
            turn = Mathf.SmoothDamp(turn, target, ref turnVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);

            // Jump, grab and landing clips take full control immediately; the blend settles in the background.
            if (!canBalance || Mathf.Abs(turn) < 0.0001f)
                return;

            spineAnimation = spine.localRotation;
            if (chest != null) chestAnimation = chest.localRotation;
            if (head != null) headAnimation = head.localRotation;
            poseApplied = true;

            Vector3 up = player.BordUpVector.normalized;
            Vector3 travel = (player.IsSwitch ? -player.BordDirection : player.BordDirection).normalized;
            Vector3 bendAxis = transform.right;
            float spineWeight = chest != null && chest != spine ? 0.4f : 1f;
            ApplyBalance(spine, spineWeight, up, travel, bendAxis);
            if (spineWeight < 1f) ApplyBalance(chest, 1f - spineWeight, up, travel, bendAxis);

            // Keep the gaze steadier than the shoulders while the torso counter-rotates.
            if (head != null && head != spine && head != chest)
                head.rotation = Quaternion.AngleAxis(turn * counterRotation * 0.3f, up) * head.rotation;
        }

        private void ApplyBalance(Transform bone, float weight, Vector3 up, Vector3 travel, Vector3 bendAxis)
        {
            Quaternion offset = Quaternion.AngleAxis(-turn * counterRotation * weight, up)
                * Quaternion.AngleAxis(-turn * turnLean * weight, travel)
                * Quaternion.AngleAxis(Mathf.Abs(turn) * turnCrouch * weight, bendAxis);
            // World axes avoid assumptions about the imported skeleton's local bone orientation.
            bone.rotation = offset * bone.rotation;
        }

        private void RestoreAnimationPose()
        {
            if (!poseApplied) return;
            if (spine != null) spine.localRotation = spineAnimation;
            if (chest != null && chest != spine) chest.localRotation = chestAnimation;
            if (head != null && head != spine && head != chest) head.localRotation = headAnimation;
            poseApplied = false;
        }

        private void OnDisable()
        {
            RestoreAnimationPose();
            turn = turnVelocity = 0f;
        }
    }
}
