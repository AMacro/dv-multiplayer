using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using System;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

[DisallowMultipleComponent]
internal class LocalPlayerTrackerNonVR : LocalPlayerTrackerBase
{

    protected override void Awake()
    {
        base.Awake();

        NetworkLifecycle.Instance.OnTick += OnTick;
        
        if (fps.Locomotion != null)
            fps.Locomotion.LeanDirectionChanged += LeanDirectionChanged;

        fps.provider.OnPlayerHeightAdjusted += OnPlayerHeightAdjusted;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (UnloadWatcher.isQuitting)
            return;

        NetworkLifecycle.Instance.OnTick -= OnTick;

        if (fps.Locomotion != null)
            fps.Locomotion.LeanDirectionChanged -= LeanDirectionChanged;

        fps.provider.OnPlayerHeightAdjusted -= OnPlayerHeightAdjusted;
    }

    private void OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading)
            return;

        if (isOnCar && car == null)
        {
            car = PlayerManager.Car;
            isOnCar = car != null;
        }

        Vector3 position = isOnCar ? PlayerManager.PlayerTransform.localPosition : PlayerManager.PlayerTransform.GetWorldAbsolutePosition();
        float rotationY = PlayerManager.PlayerCamera.transform.eulerAngles.y;

        float rawHeadPitch = fps.m_MouseLook.m_CameraTargetRot.eulerAngles.x;

        float lookPosition = rawHeadPitch > 180f ? rawHeadPitch - 360f : rawHeadPitch;
        bool lookPositionChanged = Math.Abs(lastLookPosition - lookPosition) > ROTATION_THRESHOLD;

        PlayerPostureFlags posture = PlayerPostureFlags.None;

        if (!fps.underwater)
        {
            if (fps.IsCrouching)
                posture |= PlayerPostureFlags.Crouch;
            if (isJumping)
                posture |= PlayerPostureFlags.Jump;
            if (fps.provider.IsSitting)
                posture |= PlayerPostureFlags.Sit;

            if (lean == LocomotionInputWrapper.LeanDirection.LeaningLeft)
                posture |= PlayerPostureFlags.LeanLeft;
            else if (lean == LocomotionInputWrapper.LeanDirection.LeaningRight)
                posture |= PlayerPostureFlags.LeanRight;
        }
        else
        {
            posture = PlayerPostureFlags.Swim;
        }

        ushort carNetID = isOnCar ? car.GetNetId() : (ushort)0;

        bool positionOrRotationChanged = lastPosture != posture ||
                                        lastOnCar != isOnCar ||
                                        (isOnCar && (lastCarNetId != carNetID)) ||
                                        Vector3.Distance(lastPosition, position) > 0 ||
                                        Math.Abs(lastRotationY - rotationY) > ROTATION_THRESHOLD;

        bool heightChanged = lastSitHeight != sitHeight;

        if (!positionOrRotationChanged && !lookPositionChanged && !heightChanged && sentFinalPosition)
            return;

        lastSitHeight = sitHeight;
        lastOnCar = isOnCar;
        lastCarNetId = carNetID;
        lastPosition = position;
        lastRotationY = rotationY;
        lastLookPosition = lookPosition;
        lastPosture = posture;
        sentFinalPosition = !positionOrRotationChanged;

        NetworkLifecycle.Instance.Client.SendPlayerPosition
        (
            lastPosition,
            PlayerManager.PlayerTransform.InverseTransformDirection(fps.m_MoveDir),
            lastRotationY,
            lookPosition,
            sitHeight,
            carNetID,
            posture,
            isOnCar,
            isJumping || sentFinalPosition
        );

        isJumping = false;
    }

    private static void LeanDirectionChanged(LocomotionInputWrapper.LeanDirection leanDirection)
    {
        lean = leanDirection;
    }

    private void OnPlayerHeightAdjusted(float newHeight, float _)
    {
        var clampedHeight= Mathf.Clamp(newHeight, CustomFirstPersonController.MIN_PLAYER_SITTING_HEIGHT, CustomFirstPersonController.MAX_PLAYER_SITTING_HEIGHT);
        sitHeight = Mathf.InverseLerp(CustomFirstPersonController.MIN_PLAYER_SITTING_HEIGHT, CustomFirstPersonController.MAX_PLAYER_SITTING_HEIGHT, clampedHeight);
    }
}
