using DV.Player;
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

    // The desktop controller. Null in VR (DV doesn't instantiate CustomFirstPersonController there),
    // so it's only used as an optional source for the exact movement direction.
    private static CustomFirstPersonController fps;

    private static bool lastOnCar;
    private static ushort lastCarNetId;
    private static Vector3 lastPosition;
    private static float lastRotationY;
    private static bool sentFinalPosition;

    private static bool isJumping;
    private static bool isOnCar;
    private static TrainCar car;

    private static bool subscribed;

    // Position sync is driven by the network client lifecycle, NOT by the player controller.
    // CustomFirstPersonController.Awake only fires on desktop; in VR it never runs, so hooking it
    // meant the host's position was never sent and stayed at (0,0,0) on the server — breaking every
    // proximity-gated system (station loco spawning, job generation, control authority). Subscribing
    // from StartClient (both modes) fixes that.
    internal static void SubscribePositionSync()
    {
        if (subscribed)
            return;
        subscribed = true;

        lastOnCar = false;
        lastCarNetId = 0;
        lastPosition = Vector3.zero;
        lastRotationY = 0f;
        sentFinalPosition = false;
        isJumping = false;

        NetworkLifecycle.Instance.OnTick += OnTick;
    }

    internal static void UnsubscribePositionSync()
    {
        if (!subscribed)
            return;
        subscribed = false;
        NetworkLifecycle.Instance.OnTick -= OnTick;
    }

    [HarmonyPatch(nameof(CustomFirstPersonController.Awake))]
    [HarmonyPostfix]
    private static void CharacterMovement(CustomFirstPersonController __instance)
    {
        // Desktop only: capture the controller so we can read its precise move direction.
        fps = __instance;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.OnDestroy))]
    private static void OnDestroy()
    {
        if (UnloadWatcher.isQuitting)
            return;

        fps = null;
    }

    private static void OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading)
            return;

        // Guard for readiness: on the host StartClient runs before the world/player exist, and this
        // must also work in VR where there is no CustomFirstPersonController.
        if (NetworkLifecycle.Instance.Client == null)
            return;
        Transform playerTransform = PlayerManager.PlayerTransform;
        if (playerTransform == null || PlayerManager.PlayerCamera == null)
            return;

        // Poll the current car directly (no CarChanged event dependency, so it works regardless of
        // which controller is active).
        car = PlayerManager.Car;
        isOnCar = car != null;

        // Only report the player as "on car" once we have a valid NetId for that car. Right after a
        // save load the car's NetworkedTrainCar.NetId may not be assigned yet; if we sent the car-LOCAL
        // position together with CarId 0, the server would interpret that small local offset as a
        // world-absolute position and place the player kilometres away. That breaks control-authority
        // proximity checks, so cab controls get grabbed then instantly force-released (~10ms "grip").
        // Falling back to a world-absolute position + CarId 0 keeps position, CarId and the on-car flag
        // consistent, and self-corrects on the next tick once the NetId is assigned.
        ushort carNetID = isOnCar ? car.GetNetId() : (ushort)0;
        bool onCarNetworked = isOnCar && carNetID != 0;

        Vector3 position = onCarNetworked ? playerTransform.localPosition : playerTransform.GetWorldAbsolutePosition();
        float rotationY = PlayerManager.PlayerCamera.transform.eulerAngles.y;

        bool positionOrRotationChanged = lastOnCar != onCarNetworked || (onCarNetworked && (lastCarNetId != carNetID)) || Vector3.Distance(lastPosition, position) > 0 || Math.Abs(lastRotationY - rotationY) > 0.2f;//ROTATION_THRESHOLD;

        if (!positionOrRotationChanged && sentFinalPosition)
            return;

        lastOnCar = onCarNetworked;
        lastCarNetId = carNetID;
        lastPosition = position;
        lastRotationY = rotationY;
        sentFinalPosition = !positionOrRotationChanged;

        // Move direction is only used to animate the remote avatar's walk; the desktop controller
        // exposes it exactly, in VR we don't have it so send zero (avatar still lerps toward position).
        Vector3 moveDir = fps != null ? playerTransform.InverseTransformDirection(fps.m_MoveDir) : Vector3.zero;

        NetworkLifecycle.Instance.Client.SendPlayerPosition(lastPosition, moveDir, lastRotationY, carNetID, isJumping, onCarNetworked, isJumping || sentFinalPosition);
        isJumping = false;
    }


    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.SetJumpParameters))]
    private static void SetJumpParameters()
    {
        isJumping = true;
    }
}
