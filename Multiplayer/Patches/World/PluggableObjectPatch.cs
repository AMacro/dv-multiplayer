using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(PluggableObject))]
public static class PluggableObjectPatch
{
    [HarmonyPatch(nameof(PluggableObject.Awake))]
    [HarmonyPrefix]
    public static bool Awake(PluggableObject __instance)
    {
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        // Allow the client to setup the plug, but don't allow `InstantSnapTo(this.startAttachedTo);` to be called
        __instance.CheckInitialization();
        return false;
    }

    [HarmonyPatch(nameof(PluggableObject.IsHeldInHand), MethodType.Getter)]
    [HarmonyPrefix]
    public static bool IsHeldInHand(PluggableObject __instance, ref bool __result)
    {
        var result = __result;
        Multiplayer.LogDebug(() => $"IsHeldInHand({result})");

        if (NetworkedPluggableObject.Get(__instance, out var networkedPluggableObject))
            __result = networkedPluggableObject.IsHeld;
        else
            __result = __instance.controlGrabbed;

        result = __result;
        Multiplayer.LogDebug(() => $"IsHeldInHand() result: {result}, net found: {networkedPluggableObject != null}");

        return false;
    }

    [HarmonyPatch(nameof(PluggableObject.ConnectingRoutine))]
    [HarmonyPrefix]
    public static void ConnectingRoutine(PluggableObject __instance)
    {
        Multiplayer.LogDebug(() => $"ConnectingRoutine()");
        if (NetworkedPluggableObject.Get(__instance, out var networkedPluggableObject))
        {
            networkedPluggableObject.IsConnecting = true;
        }
    }

    //[HarmonyPatch(typeof(PluggableObject), nameof(PluggableObject.InstantSnapTo))]
    //[HarmonyPrefix]
    //public static void InstantSnapTo_Prefix(PluggableObject __instance, PlugSocket socket)
    //{
    //    Multiplayer.LogDebug(() => $"PluggableObject.InstantSnapTo() called: {__instance.GetObjectPath()} -> {socket.GetObjectPath()}\r\n {Environment.StackTrace}");
    //}
}
