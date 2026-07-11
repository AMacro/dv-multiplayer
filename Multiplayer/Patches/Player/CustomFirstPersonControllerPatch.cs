using HarmonyLib;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Utils;
using System;

namespace Multiplayer.Patches.Player;

[HarmonyPatch(typeof(CustomFirstPersonController))]
public static class CustomFirstPersonControllerPatch
{
    public static Action OnJump;

    [HarmonyPatch(nameof(CustomFirstPersonController.Awake))]
    [HarmonyPostfix]
    private static void Awake(CustomFirstPersonController __instance)
    {
        LocalPlayerTrackerBase tracker;

        if (VRManager.IsVREnabled())
            tracker = __instance.GetOrAddComponent<LocalPlayerTrackerVR>();
        else
            tracker = __instance.GetOrAddComponent<LocalPlayerTrackerNonVR>();

        if (tracker == null)
            Multiplayer.LogError("Failed to add LocalPlayerTracker to CustomFirstPersonController");
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CustomFirstPersonController.SetJumpParameters))]
    private static void SetJumpParameters()
    {
        OnJump?.Invoke();
    }
}
