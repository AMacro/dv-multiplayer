using DV.RemoteControls;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Utils;
using System;
using UnityEngine;


namespace Multiplayer.Patches.World.Items;

[HarmonyPatch(typeof(RemoteControllerModule))]
public static class RemoteControllerModulePatch
{
    [HarmonyPatch(nameof(RemoteControllerModule.RemoteControllerCouple))]
    [HarmonyPostfix]
    static void RemoteControllerCouple(RemoteControllerModule __instance)
    {
        NetworkLifecycle.Instance.Client.SendCouplerInteraction(CouplerInteractionType.CoupleViaRemote, __instance.car.frontCoupler);
    }

    [HarmonyPatch(nameof(RemoteControllerModule.Uncouple))]
    [HarmonyPostfix]
    static void Uncouple(RemoteControllerModule __instance, int selectedCoupler)
    {
        TrainCar startCar = __instance.car;
        Coupler nthCouplerFrom = CouplerLogic.GetNthCouplerFrom((selectedCoupler > 0) ? startCar.frontCoupler : startCar.rearCoupler, Mathf.Abs(selectedCoupler) - 1);

        if (nthCouplerFrom != null)
        {
            NetworkLifecycle.Instance.Client.SendCouplerInteraction(CouplerInteractionType.UncoupleViaRemote, nthCouplerFrom);
        }
    }
}
