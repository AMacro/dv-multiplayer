using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;
using System.Collections.Generic;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(CarSpawner))]
public static class CarSpawner_Patch
{
    [HarmonyPatch(nameof(CarSpawner.PrepareTrainCarForDeleting))]
    [HarmonyPrefix]
    private static void PrepareTrainCarForDeleting(TrainCar trainCar)
    {
        if (UnloadWatcher.isUnloading)
            return;

        if (trainCar == null || !trainCar.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.IsDestroying = true;

        NetworkLifecycle.Instance.Server?.SendDestroyTrainCar(networkedTrainCar.NetId);
    }

    //Called from 
    [HarmonyPatch(nameof(CarSpawner.SpawnCars))]
    [HarmonyPostfix]
    private static void SpawnCars(List<TrainCar> __result)
    {
        if (UnloadWatcher.isUnloading)
            return;

        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (__result == null || __result.Count == 0)
            return;

        //Coupling is delayed by AutoCouple(), so a true trainset for the entire consist doesn't exist yet
        Multiplayer.LogDebug(() => $"SpawnCars() {__result?.Count} cars spawned, sending to players");
        NetworkLifecycle.Instance.Server.SendSpawnTrainset(__result, true, true);

    }

    [HarmonyPatch(nameof(CarSpawner.SpawnCarFromRemote))]
    [HarmonyPostfix]
    private static void SpawnCarFromRemote(TrainCar __result)
    {
        if (UnloadWatcher.isUnloading)
            return;

        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (__result == null)
            return;

        Multiplayer.LogDebug(() => $"SpawnCarFromRemote() {__result?.carLivery?.name} spawned, sending to players");
        NetworkLifecycle.Instance.Server.SendSpawnTrainset([__result], true, true);

    }

    [HarmonyPatch(nameof(CarSpawner.SpawnCarOnClosestTrack))]
    [HarmonyPostfix]
    private static void SpawnCarOnClosestTrack(TrainCar __result)
    {
        if (UnloadWatcher.isUnloading)
            return;

        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (__result == null)
            return;

        Multiplayer.LogDebug(() => $"SpawnCarOnClosestTrack() {__result?.carLivery?.name} spawned, sending to players");
        NetworkLifecycle.Instance.Server.SendSpawnTrainset([__result], true, true);

    }
}
