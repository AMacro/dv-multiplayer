using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;
using System.Diagnostics;

namespace Multiplayer.Patches.World.Items;

/*
[HarmonyPatch(typeof(Lantern))]
public static class LanternPatch
{
    [HarmonyPatch(nameof(Lantern.Awake))]
    [HarmonyPostfix]
    static void Awake(Lantern __instance)
    {
        var networkedItem = __instance?.gameObject?.GetOrAddComponent<NetworkedItem>();
        if (networkedItem == null)
        {
            Multiplayer.LogError($"LanternAwakePatch.Awake() networkedItem returned null!");
            return;
        }

        networkedItem.Initialize(__instance);
    }

    [HarmonyPatch(nameof(Lantern.Initialize))]
    [HarmonyPostfix]
    static void Initialize(Lantern __instance)
    {

        var networkedItem = __instance?.gameObject?.GetOrAddComponent<NetworkedItem>();

        if(networkedItem == null)
        {
            Multiplayer.LogError($"Lantern.Initialize() networkedItem Not Found!");
            return;
        }

        try
        {
            // Register the values you want to track with both getters and setters
            networkedItem.RegisterTrackedValue(
                    "wickSize",
                    () => __instance.wickSize,
                    value =>
                    {
                        __instance.UpdateWickRelatedLogic(value);
                    }
                    );

            networkedItem.RegisterTrackedValue(
                "Ignited",
                () => __instance.igniter.enabled,
                value =>
                        {
                            if (value)
                                __instance.Ignite(1);
                            else
                                __instance.OnFlameExtinguished();
                        }
                );

            networkedItem.FinaliseTrackedValues();

        }catch(Exception ex)
        {
            Multiplayer.LogError($"Lantern.Initialize() {ex.Message}\r\n{ex.StackTrace}");
        }
    }
}
*/
