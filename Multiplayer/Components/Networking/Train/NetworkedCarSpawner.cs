using System.Collections.Generic;
using DV.ThingTypes;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.Train;

public static class NetworkedCarSpawner
{
    public static void SpawnCars(TrainsetSpawnPart[] parts)
    {
        TrainCar[] cars = new TrainCar[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            cars[i] = SpawnCar(parts[i], true);
        for (int i = 0; i < cars.Length; i++)
            AutoCouple(parts[i], cars[i]);
    }

    public static TrainCar SpawnCar(TrainsetSpawnPart spawnPart, bool preventCoupling = false)
    {
        if (!NetworkedRailTrack.Get(spawnPart.Bogie1.TrackNetId, out NetworkedRailTrack bogie1Track) && spawnPart.Bogie1.TrackNetId != 0)
        {
            NetworkLifecycle.Instance.Client.LogDebug(() => $"Tried spawning car but couldn't find track with index {spawnPart.Bogie1.TrackNetId}");
            return null;
        }

        if (!NetworkedRailTrack.Get(spawnPart.Bogie2.TrackNetId, out NetworkedRailTrack bogie2Track) && spawnPart.Bogie2.TrackNetId != 0)
        {
            NetworkLifecycle.Instance.Client.LogDebug(() => $"Tried spawning car but couldn't find track with index {spawnPart.Bogie2.TrackNetId}");
            return null;
        }

        if (!TrainComponentLookup.Instance.LiveryFromId(spawnPart.LiveryId, out TrainCarLivery livery))
        {
            NetworkLifecycle.Instance.Client.LogDebug(() => $"Tried spawning car but couldn't find TrainCarLivery with ID {spawnPart.LiveryId}");
            return null;
        }

        (TrainCar trainCar, bool isPooled) = GetFromPool(livery);

        NetworkedTrainCar networkedTrainCar = trainCar.gameObject.GetOrAddComponent<NetworkedTrainCar>();
        networkedTrainCar.NetId = spawnPart.NetId;
        trainCar.gameObject.GetOrAddComponent<TrainSpeedQueue>();

        trainCar.gameObject.SetActive(true);

        if (isPooled)
            trainCar.AwakeForPooledCar();

        trainCar.InitializeExistingLogicCar(spawnPart.CarId, spawnPart.CarGuid);

        Transform trainTransform = trainCar.transform;
        trainTransform.position = spawnPart.Position + WorldMover.currentMove;
        trainTransform.eulerAngles = spawnPart.Rotation;
        trainCar.playerSpawnedCar = spawnPart.PlayerSpawnedCar;
        trainCar.preventAutoCouple = true;

        if (!spawnPart.Bogie1.HasDerailed)
            trainCar.Bogies[0].SetTrack(bogie1Track.RailTrack, spawnPart.Bogie1.PositionAlongTrack, spawnPart.Bogie1.TrackDirection);
        else
            trainCar.Bogies[0].SetDerailedOnLoadFlag(true);

        if (!spawnPart.Bogie2.HasDerailed)
            trainCar.Bogies[1].SetTrack(bogie2Track.RailTrack, spawnPart.Bogie2.PositionAlongTrack, spawnPart.Bogie2.TrackDirection);
        else
            trainCar.Bogies[1].SetDerailedOnLoadFlag(true);

        CarSpawner.Instance.FireCarSpawned(trainCar);

        networkedTrainCar.Client_trainSpeedQueue.ReceiveSnapshot(spawnPart.Speed, NetworkLifecycle.Instance.Tick);

        if (!preventCoupling)
            AutoCouple(spawnPart, trainCar);

        return trainCar;
    }

    private static void AutoCouple(TrainsetSpawnPart spawnPart, TrainCar trainCar)
    {
        if (spawnPart.IsFrontCoupled) trainCar.frontCoupler.TryCouple(false, true);
        if (spawnPart.IsRearCoupled) trainCar.rearCoupler.TryCouple(false, true);
    }

    private static (TrainCar, bool) GetFromPool(TrainCarLivery livery)
    {
        if (!CarSpawner.Instance.useCarPooling || !CarSpawner.Instance.carLiveryToTrainCarPool.TryGetValue(livery, out List<TrainCar> trainCarList))
            return Instantiate(livery);

        int count = trainCarList.Count;
        if (count <= 0)
            return Instantiate(livery);

        int index = count - 1;
        TrainCar trainCar = trainCarList[index];
        trainCarList.RemoveAt(index);
        CarSpawner.Instance.trainCarPoolHashSet.Remove(trainCar);

        if (trainCar != null)
        {
            Transform trainCarTransform = trainCar.transform;
            trainCarTransform.SetParent(null);
            trainCarTransform.localScale = Vector3.one;
            trainCar.gameObject.SetActive(false); // Enabled after NetworkedTrainCar has been added

            Transform interiorTransform = trainCar.interior.transform;
            interiorTransform.SetParent(null);
            interiorTransform.localScale = Vector3.one;

            trainCar.interior.gameObject.SetActive(true);
            trainCar.rb.isKinematic = false;
            return (trainCar, true);
        }

        Multiplayer.LogError($"Failed to get {livery.id} from pool!");
        return Instantiate(livery);
    }

    private static (TrainCar, bool) Instantiate(TrainCarLivery livery)
    {
        bool wasActive = livery.prefab.activeSelf;
        livery.prefab.SetActive(false);
        (TrainCar, bool) result = (Object.Instantiate(livery.prefab).GetComponent<TrainCar>(), false);
        livery.prefab.SetActive(wasActive);
        return result;
    }
}
