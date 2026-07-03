using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using System;
using UnityEngine;

namespace Multiplayer.Patches.Player;

[HarmonyPatch(typeof(CustomFirstPersonController))]
public static class CustomFirstPersonControllerPatch
{
    private const float ROTATION_THRESHOLD = 0.001f;

    private static CustomFirstPersonController fps;

    private static bool lastOnCar;
    private static ushort lastCarNetId;
    private static Vector3 lastPosition;
    private static float lastRotationY;
    private static bool sentFinalPosition;

    private static bool isJumping;
    private static bool isOnCar;
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
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.OnDestroy))]
    private static void OnDestroy()
    {
        if (UnloadWatcher.isQuitting)
            return;

        NetworkLifecycle.Instance.OnTick -= OnTick;
        PlayerManager.CarChanged -= OnCarChanged;
    }

    private static void OnCarChanged(TrainCar trainCar)
    {
        isOnCar = trainCar != null;
        car = trainCar;
    }

    private static void OnTick(uint tick)
    {
        if(UnloadWatcher.isUnloading)
            return;

        if (isOnCar && car == null)
        {
            car = PlayerManager.Car;
            isOnCar = car != null;
        }

        // Only report the player as "on car" once we have a valid NetId for that car. Right after a
        // save load the car's NetworkedTrainCar.NetId may not be assigned yet; if we sent the car-LOCAL
        // position together with CarId 0, the server would interpret that small local offset as a
        // world-absolute position and place the player kilometres away. That breaks control-authority
        // proximity checks, so cab controls get grabbed then instantly force-released (~10ms "grip").
        // Falling back to a world-absolute position + CarId 0 keeps position, CarId and the on-car flag
        // consistent, and self-corrects on the next tick once the NetId is assigned.
        ushort carNetID = isOnCar ? car.GetNetId() : (ushort)0;
        bool onCarNetworked = isOnCar && carNetID != 0;

        Vector3 position = onCarNetworked ? PlayerManager.PlayerTransform.localPosition : PlayerManager.PlayerTransform.GetWorldAbsolutePosition();
        float rotationY = PlayerManager.PlayerCamera.transform.eulerAngles.y;

        bool positionOrRotationChanged = lastOnCar != onCarNetworked || (onCarNetworked && (lastCarNetId != carNetID)) || Vector3.Distance(lastPosition, position) > 0 || Math.Abs(lastRotationY - rotationY) > 0.2f;//ROTATION_THRESHOLD;

        if (!positionOrRotationChanged && sentFinalPosition)
            return;

        lastOnCar = onCarNetworked;
        lastCarNetId = carNetID;
        lastPosition = position;
        lastRotationY = rotationY;
        sentFinalPosition = !positionOrRotationChanged;

        NetworkLifecycle.Instance.Client.SendPlayerPosition(lastPosition, PlayerManager.PlayerTransform.InverseTransformDirection(fps.m_MoveDir), lastRotationY, carNetID, isJumping, onCarNetworked, isJumping || sentFinalPosition);
        isJumping = false;
    }


    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.SetJumpParameters))]
    private static void SetJumpParameters()
    {
        isJumping = true;
    }
}
