using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using System;
using UnityEngine;

namespace Multiplayer.Patches.Player;

[HarmonyPatch(typeof(CustomFirstPersonController))]
public static class CustomFirstPersonControllerPatch
{
    private const float ROTATION_THRESHOLD = 0.2f;

    private static CustomFirstPersonController fps;

    private static bool lastOnCar;
    private static ushort lastCarNetId;
    private static Vector3 lastPosition;
    private static float lastRotationY;
    private static float lastLookPosition;
    private static bool sentFinalPosition;
    private static LocomotionInputWrapper.LeanDirection lean;
    private static PlayerPostureFlags lastPosture;

    private static bool isJumping;
    private static bool isOnCar;
    private static float sitHeight;
    private static TrainCar car;

    [HarmonyPatch(nameof(CustomFirstPersonController.Awake))]
    [HarmonyPostfix]
    private static void CharacterMovement(CustomFirstPersonController __instance)
    {
        fps = __instance;
        isOnCar = PlayerManager.Car != null;
        car = PlayerManager.Car;
        NetworkLifecycle.Instance.OnTick += OnTick;
        PlayerManager.CarChanged += OnCarChanged;

        if (fps.Locomotion != null)
        {
            fps.Locomotion.LeanDirectionChanged += LeanDirectionChanged;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.OnDestroy))]
    private static void OnDestroy()
    {
        if (UnloadWatcher.isQuitting)
            return;

        NetworkLifecycle.Instance.OnTick -= OnTick;
        PlayerManager.CarChanged -= OnCarChanged;

        if (fps.Locomotion != null)
        {
            fps.Locomotion.LeanDirectionChanged -= LeanDirectionChanged;
        }
    }

    private static void LeanDirectionChanged(LocomotionInputWrapper.LeanDirection leanDirection)
    {
        lean = leanDirection;
    }

    private static void OnCarChanged(TrainCar trainCar)
    {
        isOnCar = trainCar != null;
        car = trainCar;
    }

    private static void OnTick(uint tick)
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

        float lookPosition = 0;
        float rawHeadPitch;

        if (!VRManager.IsVREnabled())
            rawHeadPitch = fps.m_MouseLook.m_CameraTargetRot.eulerAngles.x;
        else
            rawHeadPitch = fps.m_Camera.transform.localEulerAngles.x;

        lookPosition = rawHeadPitch > 180f ? rawHeadPitch - 360f : rawHeadPitch;
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

        if (!positionOrRotationChanged && !lookPositionChanged && sentFinalPosition)
            return;

        lastOnCar = isOnCar;
        lastCarNetId = carNetID;
        lastPosition = position;
        lastRotationY = rotationY;
        lastLookPosition = lookPosition;
        lastPosture = posture;
        sentFinalPosition = !positionOrRotationChanged;

        NetworkLifecycle.Instance.Client.SendPlayerPosition(lastPosition, PlayerManager.PlayerTransform.InverseTransformDirection(fps.m_MoveDir), lastRotationY, lookPosition, carNetID, posture, isOnCar, isJumping || sentFinalPosition);
        isJumping = false;
    }


    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.SetJumpParameters))]
    private static void SetJumpParameters()
    {
        isJumping = true;
    }
}
