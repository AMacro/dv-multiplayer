using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.ModCompatibility;
using Multiplayer.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(CarSpawner))]
public static class CarSpawner_Patch
{
    private static readonly HashSet<string> carIdsWithNoUpdates = [];
    private static bool allowingCarsUpdatesCoroRunning = false;

    [HarmonyPatch(nameof(CarSpawner.PrepareTrainCarForDeleting))]
    [HarmonyPrefix]
    private static void PrepareTrainCarForDeleting(TrainCar trainCar)
    {
        if (UnloadWatcher.isUnloading)
            return;

        if (trainCar == null || !trainCar.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.IsDestroying = true;

        NetworkLifecycle.Instance.Server?.SendDestroyTrainCar(networkedTrainCar);
    }

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

        // Coupling is delayed by AutoCouple(), so a true trainset for the entire consist doesn't exist yet
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

    //gets triggered on save load or by PersJobs only
    [HarmonyPatch(nameof(CarSpawner.SpawnLoadedCar))]
    [HarmonyPostfix]
    private static void SpawnLoadedCar(TrainCar __result)
    {
        if (UnloadWatcher.isUnloading)
            return;

        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (__result == null)
            return;

        if (!WorldStreamingInit.IsLoaded)
            return;

        Multiplayer.LogDebug(() => $"SpawnLoadedCar() {__result?.carLivery?.name} spawned, sending to players");

        if (__result.TryNetworked(out var netTC))
        {
            TrainStress.globalIgnoreStressCalculation = true;
            netTC.DoNotUpdate = true;
            carIdsWithNoUpdates.Add(__result.ID);
            NetworkLifecycle.Instance.Server.SendSpawnTrainset([__result], false, true);

            if (!allowingCarsUpdatesCoroRunning)
            {
                CoroutineManager.Instance.Run(AllowingCarsUpdatesCoro());
            }
        }
    }

    private static IEnumerator AllowingCarsUpdatesCoro()
    {
        try
        {
            allowingCarsUpdatesCoroRunning = true;
            yield return null;

            Multiplayer.LogDebug(() => $"CarSpawnerPatch: waiting with newly resumed car physics updates for all cars to resume");
            yield return new WaitUntil(() => !PersistentJobs.ResumeCoroRunning);
            yield return WaitFor.SecondsRealtime(3f);
            Multiplayer.LogDebug(() => $"CarSpawnerPatch: car resuming finished, will allow physics updates for cars {(string.Join(", ", carIdsWithNoUpdates))}");

            foreach (var tcId in carIdsWithNoUpdates)
                if (NetworkedTrainCar.GetFromTrainId(tcId, out var ntc))
                    ntc.DoNotUpdate = false;
        }
        finally
        {
            TrainStress.globalIgnoreStressCalculation = false;
            allowingCarsUpdatesCoroRunning = false;
        }
    }
}
