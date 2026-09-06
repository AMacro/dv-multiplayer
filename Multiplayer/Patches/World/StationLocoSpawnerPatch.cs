using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(StationLocoSpawner), nameof(StationLocoSpawner.Start))]
public static class StationLocoSpawner_Start_Patch
{
    private static readonly WaitForSeconds CHECK_DELAY = WaitFor.Seconds(1);

    private static void Postfix(StationLocoSpawner __instance)
    {
        __instance.StartCoroutine(WaitForSetup(__instance));
    }

    private static IEnumerator WaitForSetup(StationLocoSpawner __instance)
    {
        if (!AStartGameData.carsAndJobsLoadingFinished || CarSpawner.Instance.PoolSetupInProgress)
            yield return null;
        while (NetworkLifecycle.Instance.Client == null)
            yield return null;
        if (!NetworkLifecycle.Instance.IsHost())
            yield break;
        __instance.StartCoroutine(CheckShouldSpawn(__instance));
    }

    private static IEnumerator CheckShouldSpawn(StationLocoSpawner __instance)
    {
        while (__instance != null)
        {
            yield return CHECK_DELAY;

            if (UnloadWatcher.isUnloading)
                yield break;

            bool anyoneWithinRange = __instance.spawnTrackMiddleAnchor.transform.position.AnyPlayerSqrMag() < __instance.spawnLocoPlayerSqrDistanceFromTrack;

            switch (__instance.playerEnteredLocoSpawnRange)
            {
                case false when anyoneWithinRange:
                    __instance.playerEnteredLocoSpawnRange = true;
                    SpawnLocomotives(__instance);
                    break;
                case true when !anyoneWithinRange:
                    __instance.playerEnteredLocoSpawnRange = false;
                    break;
            }
        }
    }

    private static void SpawnLocomotives(StationLocoSpawner stationLocoSpawner)
    {
        List<Car> carsFullyOnTrack = stationLocoSpawner.locoSpawnTrack.LogicTrack().GetCarsFullyOnTrack();
        if (carsFullyOnTrack.Count != 0 && carsFullyOnTrack.Exists(car => CarTypes.IsLocomotive(car.carType)))
            return;
        List<TrainCarLivery> trainCarTypes = new(stationLocoSpawner.locoTypeGroupsToSpawn[stationLocoSpawner.nextLocoGroupSpawnIndex].liveries);
        stationLocoSpawner.nextLocoGroupSpawnIndex = Random.Range(0, stationLocoSpawner.locoTypeGroupsToSpawn.Count);

        List<Car> unusedTrainCars =
            CarSpawner.Instance.SpawnCarTypesOnTrack
            (
                trainCarTypes,
                null,
                stationLocoSpawner.locoSpawnTrack,
                true,
                true,
                flipTrainConsist: stationLocoSpawner.spawnRotationFlipped
            )
            ?.Select(TC => TC.logicCar)
            ?.ToList();

        if (unusedTrainCars != null)
            UnusedTrainCarDeleter.Instance.MarkForDelete(unusedTrainCars);
    }
}

[HarmonyPatch(typeof(StationLocoSpawner), nameof(StationLocoSpawner.Update))]
public static class StationLocoSpawner_Update_Patch
{
    private static bool Prefix()
    {
        return false;
    }
}
