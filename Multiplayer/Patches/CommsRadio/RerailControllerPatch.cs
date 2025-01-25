using System.Collections;
using DV;
using DV.InventorySystem;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Components.Networking.World;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Patches.CommsRadio;

[HarmonyPatch(typeof(RerailController))]
public static class RerailControllerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(RerailController.Awake))]
    private static void OnAwake_Prefix(RerailController __instance)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        NetworkLifecycle.Instance.Server.rerailController = __instance;
    }

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

        __instance.carToRerail.TryNetworked(out NetworkedTrainCar networkedTrainCar);

        if (networkedTrainCar == null || networkedTrainCar != null && networkedTrainCar.NetId == 0)
        {
            Multiplayer.LogDebug(() => $"RerailController unable to rerail car: {__instance.carToRerail.name}, netId {networkedTrainCar?.NetId} ");
            //CommsRadioController.PlayAudioFromRadio(__instance.cancelSound, __instance.transform);
            __instance.ClearFlags();
            return false;
        }


        NetworkLifecycle.Instance.Client.SendTrainRerailRequest(
            networkedTrainCar.NetId,
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
        yield return new WaitForSecondsRealtime((NetworkLifecycle.Instance.Client.Ping * 3f)/1000);
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
        if (car != null && car.IsRerailAllowed && car.TryNetworked(out NetworkedTrainCar networkedTrainCar) && !networkedTrainCar.HasPlayers)
            return true;
        __instance.PointToCar(null);
        return false;
    }
}
