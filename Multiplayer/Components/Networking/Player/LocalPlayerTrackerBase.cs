using Multiplayer.Networking.Data;
using Multiplayer.Patches.Player;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Components.Networking.Player;

public abstract class LocalPlayerTrackerBase : MonoBehaviour
{
    internal const float ROTATION_THRESHOLD = 0.2f;

    internal static CustomFirstPersonController fps;

    internal static bool lastOnCar;
    internal static ushort lastCarNetId;
    internal static Vector3 lastPosition;
    internal static float lastRotationY;
    internal static float lastLookPosition;
    internal static float lastSitHeight;
    internal static bool sentFinalPosition;
    internal static LocomotionInputWrapper.LeanDirection lean;
    internal static PlayerPostureFlags lastPosture;

    internal static bool isJumping;
    internal static bool isOnCar;
    internal static float sitHeight;
    internal static TrainCar car;

    protected virtual void Awake()
    {
        fps = transform.GetComponent<CustomFirstPersonController>();

        if (fps == null)
        {
            Multiplayer.LogError("CustomFirstPersonController component not found. Player tracking will not work!.");
            return;
        }

        isOnCar = PlayerManager.Car != null;
        car = PlayerManager.Car;

        PlayerManager.CarChanged += OnCarChanged;
        CustomFirstPersonControllerPatch.OnJump += OnJump;
    }

    protected virtual void OnDestroy()
    {
        if (UnloadWatcher.isQuitting)
            return;

        PlayerManager.CarChanged -= OnCarChanged;
        CustomFirstPersonControllerPatch.OnJump -= OnJump;
    }

    void OnCarChanged(TrainCar trainCar)
    {
        isOnCar = trainCar != null;
        car = trainCar;
    }

    void OnJump()
    {
        isJumping = true;
    }
}
