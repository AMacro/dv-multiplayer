using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(FlashlightItem))]
public static class FlashlightItemPatch
{
    [HarmonyPatch(nameof(FlashlightItem.Start))] 
    static void Postfix(FlashlightItem __instance)
    {
        var networkedItem = __instance.gameObject.GetOrAddComponent<NetworkedItem>();
        networkedItem.Initialize(__instance);

        // Register the values you want to track with both getters and setters
        networkedItem.RegisterTrackedValue(
            "originalLightIntensity",
            () => __instance.originalLightIntensity,
            value => __instance.originalLightIntensity = value,
            serverAuthoritative: true                           //This parameter is driven by the server: true
            );

        //probably not needed as flicker can be handled locally
        //networkedItem.RegisterTrackedValue(
        //    "intensity",
        //    () => __instance.spotlight.intensity,
        //    value =>  __instance.spotlight.intensity = value,
        //    serverAuthoritative: true                           //This parameter is driven by the server: true
        //    );

        networkedItem.RegisterTrackedValue(
            "originalBeamColour",
            () => __instance.originalBeamColor.ColorToUInt32(),
            value =>__instance.originalBeamColor = value.UInt32ToColor(),
            serverAuthoritative: true                           //This parameter is driven by the server: true
            );

        networkedItem.RegisterTrackedValue(
            "beamColour",
            () => __instance.beamController.GetBeamColor().ColorToUInt32(),
            value =>
            {
                Color colour = value.UInt32ToColor();
                __instance.beamController.SetBeamColor(colour);
                __instance.spotlight.color = colour;
            },
            serverAuthoritative: true                            //This parameter is driven by the server: true
            );

        networkedItem.RegisterTrackedValue(
            "batteryPower",
            () => __instance.battery.currentPower,
            value =>
                {
                    __instance.battery.currentPower = value;     //set the value
                    __instance.battery.UpdatePower(0f);          //process a delta of 0 to force an update
                },
            (current, last) => Math.Abs(current - last) >= 1.0f, //Don't communicate updates for changes less than 1f
            true                                                 //This parameter is driven by the server: true
            );

        networkedItem.RegisterTrackedValue(
            "buttonState",
            () => (__instance.button.Value > 0f),
            value =>
                {
                    if (value)
                        __instance.button.SetValue(1f);
                    else
                        __instance.button.SetValue(0f);

                    __instance.ToggleFlashlight(value);
                }
            );

        networkedItem.FinaliseTrackedValues();
    }
}
