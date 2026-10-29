using UnityEngine;

/// <summary>Additive upper-body balance for the double-board skier controlled by CarvingController.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-90)]
public sealed class SkierTurnBalance : MonoBehaviour
{
    [Header("Upper Body Bones (automatically resolved when empty)")]
    [SerializeField] private Transform spine;
    [SerializeField] private Transform chest;
    [SerializeField] private Transform head;

    [Header("Double Board Turn Balance")]
    [SerializeField, Range(0f, 30f)] private float counterRotation = 14f;
    [SerializeField, Range(0f, 25f)] private float turnLean = 14f;
    [SerializeField, Range(0f, 12f)] private float turnCrouch = 4f;
    [SerializeField, Min(0.51f)] private float fullPoseSpeed = 9f;
    [SerializeField, Min(0.01f)] private float responseTime = 0.16f;
    [SerializeField, Min(0.01f)] private float returnTime = 0.24f;

    private CarvingController controller;
    private Animator animator;
    private float turn;
    private float turnVelocity;
    private Quaternion spineAnimation;
    private Quaternion chestAnimation;
    private Quaternion headAnimation;
    private bool poseApplied;

    public void Initialize(CarvingController movement, Animator modelAnimator)
    {
        RestoreAnimationPose();
        controller = movement;
        animator = modelAnimator;
        turn = turnVelocity = 0f;

        if (animator != null && animator.isHuman)
        {
            if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (head == null) head = animator.GetBoneTransform(HumanBodyBones.Head);
        }
        // The supplied Male Skier also has these names when imported as a Generic rig.
        foreach (Transform bone in GetComponentsInChildren<Transform>(true))
        {
            if (spine == null && bone.name == "spine_01") spine = bone;
            if (chest == null && bone.name == "spine_02") chest = bone;
            if (head == null && bone.name == "head") head = bone;
        }
        if (spine == null)
        {
            Debug.LogWarning("[SkierTurnBalance] Assign a spine bone on " + name, this);
            enabled = false;
        }
    }

    private void Update()
    {
        // Restore before the Animator evaluates, including frames when animation is culled.
        RestoreAnimationPose();
    }

    private void LateUpdate()
    {
        if (controller == null || !controller.isActiveAndEnabled || animator == null
            || !animator.isActiveAndEnabled || spine == null || Time.deltaTime <= 0f)
            return;

        bool canBalance = controller.IsGrounded && !controller.IsBraking && !IsSpecialAnimation();
        float speedWeight = Mathf.InverseLerp(0.5f, fullPoseSpeed, controller.GetCurrentSpeed());
        float target = canBalance ? Mathf.Clamp(controller.VisualTurnInput, -1f, 1f) * speedWeight : 0f;
        turn = Mathf.SmoothDamp(turn, target, ref turnVelocity,
            Mathf.Abs(target) > 0.001f ? responseTime : returnTime, Mathf.Infinity, Time.deltaTime);
        // Airborne, braking and fall/finish poses retain full control of the skeleton.
        if (!canBalance || Mathf.Abs(turn) < 0.0001f) return;

        spineAnimation = spine.localRotation;
        if (chest != null) chestAnimation = chest.localRotation;
        if (head != null) headAnimation = head.localRotation;
        poseApplied = true;

        Transform movement = controller.transform;
        float spineWeight = chest != null && chest != spine ? 0.45f : 1f;
        ApplyBalance(spine, spineWeight, movement);
        if (spineWeight < 1f) ApplyBalance(chest, 1f - spineWeight, movement);
        if (head != null && head != spine && head != chest)
            head.rotation = Quaternion.AngleAxis(turn * counterRotation * 0.3f, movement.up) * head.rotation;
    }

    private void ApplyBalance(Transform bone, float weight, Transform movement)
    {
        Quaternion offset = Quaternion.AngleAxis(-turn * counterRotation * weight, movement.up)
            * Quaternion.AngleAxis(-turn * turnLean * weight, movement.forward)
            * Quaternion.AngleAxis(Mathf.Abs(turn) * turnCrouch * weight, movement.right);
        bone.rotation = offset * bone.rotation;
    }

    private bool IsSpecialAnimation()
    {
        if (IsSpecialState(animator.GetCurrentAnimatorStateInfo(0))) return true;
        return animator.IsInTransition(0) && IsSpecialState(animator.GetNextAnimatorStateInfo(0));
    }

    private static bool IsSpecialState(AnimatorStateInfo state)
    {
        return state.IsName("Anim_Male_Skier_Braking") || state.IsName("Anim_Male_Skier_Braking_Loop")
            || state.IsName("Anim_Male_Skier_Fall") || state.IsName("Anim_Male_Skier_Losing")
            || state.IsName("Anim_Male_Skier_Winning") || state.IsName("Anim_Male_Skier_From_Losing_To_Idle")
            || state.IsName("Anim_Male_Skier_From_Winning_To_Idle");
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
