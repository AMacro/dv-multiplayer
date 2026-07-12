using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

internal class NetworkedPlayerIKHandler : MonoBehaviour
{
    private const float MAX_ARM_REACH = 0.7f;

    private Animator animator;

    public Quaternion LeftHandCorrection = Quaternion.identity;
    public Quaternion RightHandCorrection = Quaternion.identity;

    public Vector3 LeftHandPosition;
    public Quaternion LeftHandRotation = Quaternion.identity;
    public Vector3 RightHandPosition;
    public Quaternion RightHandRotation = Quaternion.identity;

    public bool IsActive { get; set; }

    protected void Awake()
    {
        animator = GetComponentInChildren<Animator>(true);
    }

    protected void OnAnimatorIK(int layerIndex)
    {
        if (!IsActive || animator == null)
            return;

        Vector3 clampedLeft = ClampToReach(AvatarIKGoal.LeftHand, LeftHandPosition);
        Vector3 clampedRight = ClampToReach(AvatarIKGoal.RightHand, RightHandPosition);

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, clampedLeft);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, LeftHandRotation);

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
        animator.SetIKPosition(AvatarIKGoal.RightHand, clampedRight);
        animator.SetIKRotation(AvatarIKGoal.RightHand, RightHandRotation);
    }

    private Vector3 ClampToReach(AvatarIKGoal goal, Vector3 worldTarget)
    {
        HumanBodyBones shoulderBone = goal == AvatarIKGoal.LeftHand
           ? HumanBodyBones.LeftUpperArm
           : HumanBodyBones.RightUpperArm;

        Transform shoulder = animator.GetBoneTransform(shoulderBone);
        if (shoulder == null)
            return worldTarget;

        Vector3 delta = worldTarget - shoulder.position;
        if (delta.magnitude > MAX_ARM_REACH)
            return shoulder.position + delta.normalized * MAX_ARM_REACH;

        return worldTarget;
    }
}
