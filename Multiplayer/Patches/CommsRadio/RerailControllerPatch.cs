using System.Collections;
using DV;
using DV.InventorySystem;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Patches.CommsRadio;

[HarmonyPatch(typeof(RerailController))]
public static class RerailControllerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(RerailController.OnUse))]
    private static bool OnUse_Prefix(RerailController __instance)
    {
        if (__instance.CurrentState != RerailController.State.ConfirmRerail)
            return true;
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;
        if (!__instance.carToRerail.IsRerailAllowed)
            return true;
        if (Inventory.Instance.PlayerMoney < __instance.rerailPrice)
            return true;

        NetworkLifecycle.Instance.Client.SendTrainRerailRequest(
            __instance.carToRerail.GetNetId(),
            NetworkedRailTrack.GetFromRailTrack(__instance.rerailTrack).NetId,
            __instance.rerailPointWorldAbsPosition,
            __instance.rerailPointWorldForward
        );

        CoroutineManager.Instance.StartCoroutine(PlayerSoundsLater(__instance));
        __instance.ClearFlags();
        return false;
    }

    private static IEnumerator PlayerSoundsLater(RerailController __instance)
    {
        yield return new WaitForSecondsRealtime(NetworkLifecycle.Instance.Client.Ping * 2);
        if (__instance.moneyRemovedSound != null)
            __instance.moneyRemovedSound.Play2D();
        CommsRadioController.PlayAudioFromCar(__instance.rerailingSound, __instance.carToRerail);
        CommsRadioController.PlayAudioFromRadio(__instance.confirmSound, __instance.transform);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(RerailController.OnUpdate))]
    private static bool OnUpdate_Prefix(RerailController __instance)
    {
        if (__instance.CurrentState != RerailController.State.DerailedCarScan)
            return true;
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;
        if (!Physics.Raycast(__instance.signalOrigin.position, __instance.signalOrigin.forward, out __instance.hit, RerailController.SIGNAL_RANGE, __instance.trainCarMask))
            return true;
        TrainCar car = TrainCar.Resolve(__instance.hit.transform.root);
        if (car != null && car.IsRerailAllowed && !car.Networked().HasPlayers)
            return true;
        __instance.PointToCar(null);
        return false;
    }
}
