using Multiplayer.Networking.Data.Player;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

[DisallowMultipleComponent]
internal class LocalPlayerTrackerNonVR : LocalPlayerTrackerBase
{
    private LocomotionInputWrapper.LeanDirection lean;

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (UnloadWatcher.isQuitting)
            return;

        if (fps?.Locomotion != null)
            fps.Locomotion.LeanDirectionChanged -= LeanDirectionChanged;

        if (fps?.provider != null)
            fps.provider.OnPlayerHeightAdjusted -= OnPlayerHeightAdjusted;
    }

    protected override void Initialize()
    {
        if (fps.Locomotion != null)
            fps.Locomotion.LeanDirectionChanged += LeanDirectionChanged;

        fps.provider.OnPlayerHeightAdjusted += OnPlayerHeightAdjusted;
    }

    protected override float GetHeadPitch()
    {
        return fps.m_MouseLook.m_CameraTargetRot.eulerAngles.x;
    }

    protected override void BuildPosture(ref PlayerPostureFlags posture)
    {
        if (lean == LocomotionInputWrapper.LeanDirection.LeaningLeft)
            posture |= PlayerPostureFlags.LeanLeft;
        else if (lean == LocomotionInputWrapper.LeanDirection.LeaningRight)
            posture |= PlayerPostureFlags.LeanRight;

        if (fps.provider.IsSitting)
            posture |= PlayerPostureFlags.Sit;
    }

    private void LeanDirectionChanged(LocomotionInputWrapper.LeanDirection leanDirection)
    {
        lean = leanDirection;
    }

    private void OnPlayerHeightAdjusted(float newHeight, float _)
    {
        var clampedHeight= Mathf.Clamp(newHeight, CustomFirstPersonController.MIN_PLAYER_SITTING_HEIGHT, CustomFirstPersonController.MAX_PLAYER_SITTING_HEIGHT);
        sitHeight = Mathf.InverseLerp(CustomFirstPersonController.MIN_PLAYER_SITTING_HEIGHT, CustomFirstPersonController.MAX_PLAYER_SITTING_HEIGHT, clampedHeight);
    }
}
