using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(Lighter))]
public static class LighterPatch
{
    [HarmonyPatch(nameof(Lighter.Start))]
    [HarmonyPostfix]
    static void Start(Lighter __instance)
    {
        var netItem = __instance.gameObject.GetOrAddComponent<NetworkedItem>();
        netItem.Initialize(__instance);

        Lighter lighter = __instance;

        // Register the values you want to track with both getters and setters
        netItem.RegisterTrackedValue(
            "isOpen",
            () => lighter.isOpen,
            value =>
            {
                bool active = lighter.gameObject.activeInHierarchy;
                if (active)
                {
                    if (value)
                        lighter.OpenLid();
                    else
                        lighter.CloseLid();
                }
                else
                {
                    lighter.isOpen = value;
                    if (!value)
                        lighter.CloseLid(true);
                }
            }
            );

        netItem.RegisterTrackedValue(
            "Ignited",
            () => lighter.IsFireOn(),
            value =>
            {
                bool active = lighter.gameObject.activeInHierarchy;
                if (active)
                {
                    if (value)
                        lighter.LightFire(true, true);
                    else
                        lighter.flame.UpdateFlameIntensity(0f, true);
                }
                else
                {
                    if (value && lighter.isOpen)
                    {
                        lighter.flame.UpdateFlameIntensity(1f, true);
                        lighter.OnFlameIgnited();

                    }
                    else
                    {
                        lighter.flame.UpdateFlameIntensity(0f, true);
                        lighter.OnFlameExtinguished();
                    }
                }
            }
            );

        netItem.FinaliseTrackedValues();
    }

    [HarmonyPatch(nameof(Lighter.OnEnable))]
    [HarmonyPostfix]

    static void OnEnable(Lighter __instance)
    {
        if (__instance.isOpen)
        {
            __instance.lighterAnimator.Play("lighter_case_top_open", 0);
        }
    }
}
