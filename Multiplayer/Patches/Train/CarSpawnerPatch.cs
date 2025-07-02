using System.Collections;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;
using System.Collections.Generic;
using DV.Utils;
using UnityEngine;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(CarSpawner))]
public static class CarSpawner_Patch
{
    public static List<string> DeleteWithOutPatch = new();

    [HarmonyPatch(nameof(CarSpawner.PrepareTrainCarForDeleting))]
    [HarmonyPrefix]
    private static void PrepareTrainCarForDeleting(TrainCar trainCar)
    {
        Multiplayer.Log("Deleting Train Car List "+DeleteWithOutPatch);
        if (!NetworkLifecycle.Instance.IsHost() && DeleteWithOutPatch.Contains(trainCar.ID))
        {
            Multiplayer.Log("Deleting Train Car"+trainCar.ID);
            DeleteWithOutPatch.Remove(trainCar.ID);
            return;
        }
        if (UnloadWatcher.isUnloading)
            return;

        if (trainCar == null || !trainCar.TryNetworked(out NetworkedTrainCar networkedTrainCar))
            return;

        networkedTrainCar.IsDestroying = true;

        NetworkLifecycle.Instance.Server?.SendDestroyTrainCar(networkedTrainCar);
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
        {
            Multiplayer.LogDebug(() => $"SpawnCarFromRemote() {__result?.carLivery?.name} spawned, sending to players");

            SingletonBehaviour<CoroutineManager>.Instance.Run(TransferCarToHost(__result));
            return;
        }

        if (__result == null)
            return;

        Multiplayer.LogDebug(() => $"SpawnCarFromRemote() {__result?.carLivery?.name} spawned, sending to players");
        NetworkLifecycle.Instance.Server.SendSpawnTrainset([__result], true, true);

    }

    private static IEnumerator TransferCarToHost(TrainCar trainCar)
    {
        yield return (object) WaitFor.Seconds(0.0f);
        NetworkLifecycle.Instance.Client.SendTrainsetSpawnRequestPacket([trainCar], true);
        DeleteWithOutPatch.Add(trainCar.ID);
        CarSpawner._instance.DeleteCar(trainCar);
    }

    [HarmonyPatch(nameof(CarSpawner.SpawnCarOnClosestTrack))]
    [HarmonyPostfix]
    private static void SpawnCarOnClosestTrack(TrainCar __result)
    {
        Multiplayer.Log("Test");
        Multiplayer.LogError("SpawnCarOnClosestTrack()" + __result + " and " +UnloadWatcher.isUnloading+ " and " +!NetworkLifecycle.Instance.IsHost());


        if (UnloadWatcher.isUnloading)
            return;

        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (__result == null)
            return;

        Multiplayer.LogDebug(() => $"SpawnCarOnClosestTrack() {__result?.carLivery?.name} spawned, sending to players");
        Multiplayer.LogError(__result?.carLivery?.name + " spawned, sending to players "+ __result.transform.position);
        NetworkLifecycle.Instance.Server.SendSpawnTrainset([__result], true, true);

    }
}
