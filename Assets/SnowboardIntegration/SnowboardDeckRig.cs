using UnityEngine;

/// <summary>Keep the single snowboard and its bindings fitted to the retargeted boots.</summary>
[DefaultExecutionOrder(100)]
public sealed class SnowboardDeckRig : MonoBehaviour
{
    public Animator animator;
    public Transform deck;
    public Transform leftBinding;
    public Transform rightBinding;
    public float soleOffset = .09f;
    private Transform leftFoot, rightFoot, leftToes, rightToes;

    private void Awake() { Initialize(); }

    public void Initialize()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
    }

    private void LateUpdate() { Fit(); }

    public void Fit()
    {
        if (leftFoot == null || rightFoot == null || deck == null) return;
        Vector3 along = rightFoot.position - leftFoot.position;
        if (along.sqrMagnitude < .001f) along = transform.right;
        along.Normalize();
        Vector3 up = Vector3.ProjectOnPlane(transform.up, along).normalized;
        if (up.sqrMagnitude < .001f) up = transform.up;
        deck.SetPositionAndRotation((leftFoot.position + rightFoot.position) * .5f - up * soleOffset,
            Quaternion.LookRotation(Vector3.Cross(along, up), up));
        FitBinding(leftBinding, leftFoot, leftToes, up);
        FitBinding(rightBinding, rightFoot, rightToes, up);
    }

    private void FitBinding(Transform binding, Transform foot, Transform toes, Vector3 up)
    {
        if (binding == null) return;
        Vector3 forward = toes != null ? Vector3.ProjectOnPlane(toes.position - foot.position, up) : transform.forward;
        if (forward.sqrMagnitude < .001f) forward = transform.forward;
        binding.SetPositionAndRotation(foot.position - up * (soleOffset - .02f), Quaternion.LookRotation(forward.normalized, up));
    }
}
