using System.Collections;
using DV;
using DV.InventorySystem;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Patches.CommsRadio;

[HarmonyPatch(typeof(CommsRadioCarDeleter))]
public static class CommsRadioCarDeleterPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CommsRadioCarDeleter.OnUse))]
    private static bool OnUse_Prefix(CommsRadioCarDeleter __instance)
    {
        if (__instance.CurrentState != CommsRadioCarDeleter.State.ConfirmDelete)
            return true;
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;
        if (Inventory.Instance.PlayerMoney < __instance.removePrice)
            return true;
        if (__instance.carToDelete.Networked().HasPlayers)
        {
            CommsRadioController.PlayAudioFromRadio(__instance.cancelSound, __instance.transform);
            __instance.ClearFlags();
            return false;
        }

        NetworkLifecycle.Instance.Client.SendTrainDeleteRequest(__instance.carToDelete.GetNetId());

        CoroutineManager.Instance.StartCoroutine(PlaySoundsLater(__instance, __instance.carToDelete.transform.position, __instance.removePrice > 0));
        __instance.ClearFlags();
        return false;
    }

    private static IEnumerator PlaySoundsLater(CommsRadioCarDeleter __instance, Vector3 trainPosition, bool playMoneyRemovedSound = true)
    {
        yield return new WaitForSecondsRealtime(NetworkLifecycle.Instance.Client.Ping * 2);
        if (playMoneyRemovedSound && __instance.moneyRemovedSound != null)
            __instance.moneyRemovedSound.Play2D();
        // The TrainCar may already be deleted when we're done waiting, so we play the sound manually.
        __instance.removeCarSound.Play(trainPosition, minDistance: CommsRadioController.CAR_AUDIO_SOURCE_MIN_DISTANCE, parent: WorldMover.Instance.originShiftParent);
        CommsRadioController.PlayAudioFromRadio(__instance.confirmSound, __instance.transform);
    }


    [HarmonyPrefix]
    [HarmonyPatch(nameof(CommsRadioCarDeleter.OnUpdate))]
    private static bool OnUpdate_Prefix(CommsRadioCarDeleter __instance)
    {
        if (__instance.CurrentState != CommsRadioCarDeleter.State.ScanCarToDelete)
            return true;
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;
        if (!Physics.Raycast(__instance.signalOrigin.position, __instance.signalOrigin.forward, out __instance.hit, CommsRadioCarDeleter.SIGNAL_RANGE, __instance.trainCarMask))
            return true;
        TrainCar car = TrainCar.Resolve(__instance.hit.transform.root);
        if (car != null && !car.Networked().HasPlayers)
            return true;
        __instance.PointToCar(null);
        return false;
    }
}
