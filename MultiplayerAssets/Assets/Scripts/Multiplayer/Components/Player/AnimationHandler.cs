using JetBrains.Annotations;
using UnityEngine;

namespace Multiplayer.Editor.Components.Player
{
    public class AnimationHandler : MonoBehaviour
    {
        [SerializeField]
        private Animator animator;

        private static readonly int hash_Jump = Animator.StringToHash("Jump");
        private static readonly int hash_Vertical = Animator.StringToHash("Vertical");
        private static readonly int hash_Horizontal = Animator.StringToHash("Horizontal");
        private static readonly int hash_Crouch = Animator.StringToHash("Crouch");
        private static readonly int hash_Sit = Animator.StringToHash("Sit");
        private static readonly int hash_Swim = Animator.StringToHash("Swim");
        private static readonly int hash_SitHeight = Animator.StringToHash("SitHeight");

        [UsedImplicitly]
        public void SetIsJumping(bool isJumping)
        {
            animator.SetBool(hash_Jump, isJumping);
        }

        [UsedImplicitly]
        public void SetIsCrouching(bool isCrouching)
        {
            animator.SetBool(hash_Crouch, isCrouching);
        }

        [UsedImplicitly]
        public void SetIsSitting(bool isSitting)
        {
            animator.SetBool(hash_Sit, isSitting);
        }

        [UsedImplicitly]
        public void SetIsSwimming(bool isSwimming)
        {
            animator.SetBool(hash_Swim, isSwimming);
        }

        [UsedImplicitly]
        public void SetMoveDir(Vector2 moveDir)
        {
            animator.SetFloat(hash_Horizontal, moveDir.x);
            animator.SetFloat(hash_Vertical, moveDir.y);
        }

        [UsedImplicitly]
        public void SetSitHeight(float sitHeight)
        {
            animator.SetFloat(hash_SitHeight, sitHeight);
        }

#if UNITY_EDITOR
        bool isJumping;
        bool isCrouching;
        bool isSitting;
        bool isSwimming;
        [UsedImplicitly]
        void Update()
        {

            isJumping = Input.GetButton("Jump");
            SetIsJumping(isJumping);

            isCrouching = Input.GetButton("Crouch");
            SetIsCrouching(isCrouching);

            isSitting = Input.GetButton("Sit");
            SetIsSitting(isSitting);

            isSwimming = Input.GetKey(KeyCode.V);
            SetIsSwimming(isSwimming);

            SetMoveDir(new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")));
        }
#endif
    }
}
