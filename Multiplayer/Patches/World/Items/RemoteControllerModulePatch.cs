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
    [HarmonyPrefix]
    static void Uncouple(RemoteControllerModule __instance, int selectedCoupler)
    {
        Multiplayer.LogDebug(() => $"RemoteControllerModule.Uncouple({selectedCoupler})");

        TrainCar startCar = __instance.car;

        if (startCar == null)
        {
            Multiplayer.LogWarning($"Trying to Uncouple from Remote with no paired loco");
            return;
        }

        Coupler nthCouplerFrom = CouplerLogic.GetNthCouplerFrom((selectedCoupler > 0) ? startCar.frontCoupler : startCar.rearCoupler, Mathf.Abs(selectedCoupler) - 1);

        Multiplayer.LogDebug(() => $"RemoteControllerModule.Uncouple({startCar?.ID}, {selectedCoupler}) nthCouplerFrom: [{nthCouplerFrom?.train?.ID}, {nthCouplerFrom?.train?.GetNetId()}]");
        if (nthCouplerFrom != null)
        {
            NetworkLifecycle.Instance.Client.SendCouplerInteraction(CouplerInteractionType.UncoupleViaRemote, nthCouplerFrom);
        }
    }
}
