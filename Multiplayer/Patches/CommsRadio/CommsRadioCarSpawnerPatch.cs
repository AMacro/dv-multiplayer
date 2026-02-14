using DV;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using UnityEngine;

namespace Multiplayer.Patches.CommsRadio;

[HarmonyPatch(typeof(CommsRadioCarSpawner))]
public static class CommsRadioCarSpawnerPatch
{
    public static AudioClip SpawnVehicleSound { get; private set; }
    public static AudioClip ConfirmSound { get; private set; }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CommsRadioCarSpawner.Awake))]
    private static void Awake(CommsRadioCarSpawner __instance)
    {
        SpawnVehicleSound = __instance.spawnVehicleSound;
        ConfirmSound = __instance.confirmSound;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CommsRadioCarSpawner.OnUse))]
    private static bool OnUse_Prefix(CommsRadioCarSpawner __instance)
    {
        if (__instance.state != CommsRadioCarSpawner.State.PickDestination)
            return true;

        if (NetworkLifecycle.Instance.IsHost())
            return true;

        if (__instance.spawnCooldownActive)
            return false;

        if (!__instance.canSpawnAtPoint)
        {
            __instance.ClearFlags();
            CommsRadioController.PlayAudioFromRadio(__instance.cancelSound, __instance.transform);
            return false;
        }

        if (__instance.destinationTrack == null || !__instance.closestPointOnDestinationTrack.HasValue)
        {
            SpawnFail(__instance, "CommsRadioCarSpawner unable to spawn car, destination track does not exist");
            return false;
        }

        if (!NetworkedRailTrack.TryGetNetId(__instance.destinationTrack, out var trackNetId) || trackNetId == 0)
        {
            SpawnFail(__instance, $"CommsRadioCarSpawner NetworkedRailTrack not found for: {__instance.destinationTrack?.name}");
            return false;
        }

        if (__instance.carPrefabToSpawn == null)
        {
            SpawnFail(__instance, "CommsRadioCarSpawner car prefab not found");
            return false;
        }

        if (!__instance.carPrefabToSpawn.TryGetComponent(out TrainCar trainCar) || trainCar == null)
        {
            SpawnFail(__instance, "CommsRadioCarSpawner car prefab does not have a TrainCar component");
            return false;
        }

        if (trainCar.carLivery == null)
        {
            SpawnFail(__instance, "CommsRadioCarSpawner TrainCar does not have a valid carLivery");
            return false;
        }

        NetworkLifecycle.Instance.Client.SendTrainSpawnRequest(
            trainCar.carLivery.id,
            trackNetId,
            __instance.closestPointOnDestinationTrack.Value.index,
            __instance.spawnWithTrackDirection
        );

        __instance.ClearFlags();

        return false;
    }

    private static void SpawnFail(CommsRadioCarSpawner instance, string message)
    {
        Multiplayer.LogWarning(message);
        Multiplayer.LogDebug(() => $"CommsRadioCarSpawner.OnUse() spawnCategory: {instance.category}, selectedLocoIndex: {instance.selectedLocoIndex}, selectedCarTypeIndex: {instance.selectedCarTypeIndex}, selectedLiveryIndex: {instance.selectedCarLiveryIndex}");

        CommsRadioController.PlayAudioFromRadio(instance.cancelSound, instance.transform);
        instance.ClearFlags();
    }
}
