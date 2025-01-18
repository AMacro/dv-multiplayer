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

[HarmonyPatch(typeof(CommsRadioCrewVehicle))]
public static class CommsRadioCrewVehiclePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CommsRadioCrewVehicle.OnUse))]
    private static bool OnUse_Prefix(CommsRadioCrewVehicle __instance)
    {
        if (__instance.CurrentState != CommsRadioCrewVehicle.State.ConfirmSummon)
            return true;
        if (NetworkLifecycle.Instance.IsHost())
            return true;
        if (Inventory.Instance.PlayerMoney < __instance.SummonPrice)
            return true;

        //temporarily disable client spawning
        CommsRadioController.PlayAudioFromRadio(__instance.cancelSound, __instance.transform);
        __instance.ClearFlags();
        return false;

        /*
        if(!NetworkedRailTrack.TryGetFromRailTrack(__instance.destinationTrack, out NetworkedRailTrack netRailTrack))
        {
            Multiplayer.LogError($"CommsRadioCrewVehicle unable to spawn car, NetworkedRailTrack not found for: {__instance.destinationTrack.name}");
            CommsRadioController.PlayAudioFromRadio(__instance.cancelSound, __instance.transform);
            __instance.ClearFlags();
            return false;
        }

        Vector3 absPos = (Vector3)__instance.closestPointOnDestinationTrack.Value.position;
        Vector3 fwd = __instance.closestPointOnDestinationTrack.Value.forward;

        NetworkLifecycle.Instance.Client.SendTrainSpawnRequest(__instance.selectedCar.livery.id, netRailTrack.NetId, absPos, fwd);

        CoroutineManager.Instance.StartCoroutine(PlaySoundsLater(__instance, absPos, __instance.SummonPrice > 0));
        __instance.ClearFlags();

        */
        return false;
    }

    private static IEnumerator PlaySoundsLater(CommsRadioCrewVehicle __instance, Vector3 trainPosition, bool playMoneyRemovedSound = true)
    {
        yield return new WaitForSecondsRealtime((NetworkLifecycle.Instance.Client.Ping * 3f)/1000);
        if (playMoneyRemovedSound && __instance.moneyRemovedSound != null)
            __instance.moneyRemovedSound.Play2D();
        // The TrainCar may already be deleted when we're done waiting, so we play the sound manually.
        //__instance.removeCarSound.Play(trainPosition, minDistance: CommsRadioController.CAR_AUDIO_SOURCE_MIN_DISTANCE, parent: WorldMover.Instance.originShiftParent);
        CommsRadioController.PlayAudioFromRadio(__instance.confirmSound, __instance.transform);
    }
}
