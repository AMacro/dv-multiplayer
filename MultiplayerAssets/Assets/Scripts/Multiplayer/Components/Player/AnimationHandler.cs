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
        public void SetMoveDir(Vector2 moveDir)
        {
            animator.SetFloat(hash_Horizontal, moveDir.x);
            animator.SetFloat(hash_Vertical, moveDir.y);
        }

#if UNITY_EDITOR
        bool isJumping;
        bool isCrouching;
        bool isSitting;
        [UsedImplicitly]
        void Update()
        {
            if (Input.GetButton("Jump"))
            {
                isJumping = !isJumping;
                SetIsJumping(isJumping);
            }
            else if (Input.GetButton("Crouch"))
            {
                isCrouching = !isCrouching;
                Debug.Log($"Crouch {isCrouching}");
                SetIsCrouching(isCrouching);
            }
            else if (Input.GetButton("Sit"))
            {
                isSitting = !isSitting;
                Debug.Log($"Sit {isSitting}");
                SetIsCrouching(isCrouching);
            }
        }
#endif
    }
}
