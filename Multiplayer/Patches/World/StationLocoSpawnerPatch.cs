using System.Collections;
using System.Collections.Generic;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
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
        if (!AStartGameData.carsAndJobsLoadingFinished || SingletonBehaviour<CarSpawner>.Instance.PoolSetupInProgress)
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

            bool anyoneWithinRange = IsAnyoneWithinRange(__instance, __instance.spawnTrackMiddleAnchor.transform.position);

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

    private static bool IsAnyoneWithinRange(StationLocoSpawner stationLocoSpawner, Vector3 targetPosition)
    {
        foreach (ServerPlayer serverPlayer in NetworkLifecycle.Instance.Server.ServerPlayers)
            if ((serverPlayer.WorldPosition - targetPosition).sqrMagnitude < stationLocoSpawner.spawnLocoPlayerSqrDistanceFromTrack)
                return true;
        return false;
    }

    private static void SpawnLocomotives(StationLocoSpawner stationLocoSpawner)
    {
        List<Car> carsFullyOnTrack = stationLocoSpawner.locoSpawnTrack.logicTrack.GetCarsFullyOnTrack();
        if (carsFullyOnTrack.Count != 0 && carsFullyOnTrack.Exists(car => CarTypes.IsLocomotive(car.carType)))
            return;
        List<TrainCarLivery> trainCarTypes = new(stationLocoSpawner.locoTypeGroupsToSpawn[stationLocoSpawner.nextLocoGroupSpawnIndex].liveries);
        stationLocoSpawner.nextLocoGroupSpawnIndex = Random.Range(0, stationLocoSpawner.locoTypeGroupsToSpawn.Count);
        List<TrainCar> unusedTrainCars =
            SingletonBehaviour<CarSpawner>.Instance.SpawnCarTypesOnTrack(trainCarTypes, null, stationLocoSpawner.locoSpawnTrack, true, true, flipTrainConsist: stationLocoSpawner.spawnRotationFlipped);
        if (unusedTrainCars != null)
            SingletonBehaviour<UnusedTrainCarDeleter>.Instance.MarkForDelete(unusedTrainCars);
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
