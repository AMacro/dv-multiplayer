using System.Collections;
using DV;
using DV.InventorySystem;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Patches.CommsRadio;


[HarmonyPatch(typeof(CommsRadioCarSpawner))]
public static class CommsRadioCarSpawnerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CommsRadioCarSpawner.OnUse))]
    private static bool OnUse_Prefix(CommsRadioCarSpawner __instance)
    {
        if (__instance.state != CommsRadioCarSpawner.State.PickDestination)
            return true;
        if (NetworkLifecycle.Instance.IsHost())
            return true;

        //temporarily disable client spawning
        CommsRadioController.PlayAudioFromRadio(__instance.cancelSound, __instance.transform);
        __instance.ClearFlags();
        return false;

    }
}

    //private static IEnumerator PlaySoundsLater(CommsRadioCarDeleter __instance, Vector3 trainPosition, bool playMoneyRemovedSound = true)
    //{
    //    yield return new WaitForSecondsRealtime((NetworkLifecycle.Instance.Client.Ping * 3f)/1000);
    //    if (playMoneyRemovedSound && __instance.moneyRemovedSound != null)
    //        __instance.moneyRemovedSound.Play2D();
    //    // The TrainCar may already be deleted when we're done waiting, so we play the sound manually.
    //    __instance.removeCarSound.Play(trainPosition, minDistance: CommsRadioController.CAR_AUDIO_SOURCE_MIN_DISTANCE, parent: WorldMover.Instance.originShiftParent);
    //    CommsRadioController.PlayAudioFromRadio(__instance.confirmSound, __instance.transform);
    //}


