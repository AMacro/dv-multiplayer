using DV.CabControls;
using HarmonyLib;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(Shovel))]
public static class ShovelPatch
{
    [HarmonyPatch(nameof(Shovel.Start))]
    [HarmonyPostfix]
    static void Start(Shovel __instance)
    {
        var netItem = __instance.gameObject.GetOrAddComponent<NetworkedItem>();

        netItem.Initialize(__instance);

        ShovelNonPhysicalCoal shovelNonPhysicalCoal = __instance.GetComponent<ShovelNonPhysicalCoal>();
        if( shovelNonPhysicalCoal == null)
        {
            Multiplayer.LogWarning($"Shovel.Start() netId: {netItem.NetId} Failed to find ShovelNonPhysicalCoal");
            return;
        }

        // Register the values you want to track with both getters and setters
        netItem.RegisterTrackedValue(
            "coalMassCapacity",
            () => shovelNonPhysicalCoal.coalMassCapacity,
            value =>
            {
                shovelNonPhysicalCoal.coalMassCapacity = value;
            }
            );

        netItem.RegisterTrackedValue(
            "coalMassLoaded",
            () => shovelNonPhysicalCoal.coalMassLoaded,
            value =>
            {
                shovelNonPhysicalCoal.coalMassLoaded = value;
            }
            );

        netItem.FinaliseTrackedValues();
    }

}
