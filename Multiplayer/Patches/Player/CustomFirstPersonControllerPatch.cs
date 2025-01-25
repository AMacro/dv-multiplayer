using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Patches.Player;

[HarmonyPatch(typeof(CustomFirstPersonController))]
public static class CustomFirstPersonControllerPatch
{
    private static CustomFirstPersonController fps;

    private static Vector3 lastPosition;
    private static float lastRotationY;
    private static bool sentFinalPosition;

    private static bool isJumping;
    private static bool isOnCar;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.Awake))]
    private static void CharacterMovement(CustomFirstPersonController __instance)
    {
        fps = __instance;
        isOnCar = PlayerManager.Car != null;
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
        NetworkLifecycle.Instance.Client.SendPlayerCar(!isOnCar ? (ushort)0 : trainCar.GetNetId());
    }

    private static void OnTick(uint tick)
    {
        Vector3 position = isOnCar ? PlayerManager.PlayerTransform.localPosition : PlayerManager.GetWorldAbsolutePlayerPosition();
        float rotationY = (isOnCar ? PlayerManager.PlayerTransform.localEulerAngles : PlayerManager.PlayerTransform.eulerAngles).y;

        bool positionOrRotationChanged = lastPosition != position || !Mathf.Approximately(lastRotationY, rotationY);
        if (!positionOrRotationChanged && sentFinalPosition)
            return;

        lastPosition = position;
        lastRotationY = rotationY;
        sentFinalPosition = !positionOrRotationChanged;
        NetworkLifecycle.Instance.Client.SendPlayerPosition(lastPosition, PlayerManager.PlayerTransform.InverseTransformDirection(fps.m_MoveDir), lastRotationY, isJumping, isOnCar, isJumping || sentFinalPosition);
        isJumping = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.SetJumpParameters))]
    private static void SetJumpParameters()
    {
        isJumping = true;
    }
}
