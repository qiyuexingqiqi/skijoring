using UnityEngine;

/// <summary>Reuse the clothed skier mesh while retaining the original single-board animation/physics rig.</summary>
[DefaultExecutionOrder(0)]
public sealed class SnowboardCharacterRetarget : MonoBehaviour
{
    public Animator source;
    public Animator clothedRider;
    private HumanPoseHandler sourcePose;
    private HumanPoseHandler targetPose;
    private HumanPose pose;
    private Transform sourceLeft, sourceRight, targetLeft, targetRight;

    private void Awake() { InitializePoseTransfer(); }

    public void InitializePoseTransfer()
    {
        ReleasePoseTransfer();
        if (source == null) source = GetComponent<Animator>();
        if (source == null || clothedRider == null || !source.isHuman || !clothedRider.isHuman)
        {
            Debug.LogError("Snowboard retargeting requires two valid Humanoid avatars.", this);
            enabled = false;
            return;
        }
        source.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        clothedRider.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        sourcePose = new HumanPoseHandler(source.avatar, source.transform);
        targetPose = new HumanPoseHandler(clothedRider.avatar, clothedRider.transform);
        sourceLeft = source.GetBoneTransform(HumanBodyBones.LeftFoot);
        sourceRight = source.GetBoneTransform(HumanBodyBones.RightFoot);
        targetLeft = clothedRider.GetBoneTransform(HumanBodyBones.LeftFoot);
        targetRight = clothedRider.GetBoneTransform(HumanBodyBones.RightFoot);
    }

    private void LateUpdate() { SyncPose(); }

    public void SyncPose()
    {
        if (sourcePose == null || targetPose == null || source == null || clothedRider == null) return;
        sourcePose.GetHumanPose(ref pose);
        // The two avatar roots have different parents: the clothed avatar is nested
        // inside the driver. Convert the body rotation between their parent frames
        // so the driver's heading/lean is not applied a second time to the suit.
        Quaternion sourceParent = source.transform.parent != null ? source.transform.parent.rotation : Quaternion.identity;
        Quaternion targetParent = clothedRider.transform.parent != null ? clothedRider.transform.parent.rotation : Quaternion.identity;
        pose.bodyRotation = Quaternion.Inverse(targetParent) * sourceParent * pose.bodyRotation;
        targetPose.SetHumanPose(ref pose);
        // Retargeting different body proportions must not lift the boots away from the board.
        if (sourceLeft != null && sourceRight != null && targetLeft != null && targetRight != null)
            clothedRider.transform.position += ((sourceLeft.position + sourceRight.position) - (targetLeft.position + targetRight.position)) * .5f;
    }

    private void OnDestroy() { ReleasePoseTransfer(); }

    public void ReleasePoseTransfer()
    {
        sourcePose?.Dispose();
        targetPose?.Dispose();
        sourcePose = targetPose = null;
    }
}
