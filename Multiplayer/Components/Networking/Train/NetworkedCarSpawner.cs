using DV;
using DV.JObjectExtstensions;
using DV.LocoRestoration;
using DV.Simulation.Brake;
using DV.ThingTypes;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Patches.CommsRadio;
using Multiplayer.Utils;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.Train;

public static class NetworkedCarSpawner
{
    private static readonly List<RestorationData> _restorationData = [];

    static NetworkedCarSpawner()
    {
        SceneSwitcher.SceneRequested += (DVScenes scene) =>
        {
            Multiplayer.LogDebug(() => $"NetworkedCarSpawner Scene switch requested: {scene}");

            if (scene == DVScenes.MainMenu)
                _restorationData.Clear();
        };
    }

    public static void SpawnCars(TrainsetSpawnPart[] parts, bool autoCouple, bool playerSpawned = false)
    {
        NetworkedTrainCar[] cars = new NetworkedTrainCar[parts.Length];

        // Spawn the cars
        for (int i = 0; i < parts.Length; i++)
        {
            cars[i] = SpawnCar(parts[i], true);

            if (parts[i].PlayerSpawnedCar && CommsRadioCarSpawnerPatch.SpawnVehicleSound != null)
                CommsRadioController.PlayAudioFromCar(CommsRadioCarSpawnerPatch.SpawnVehicleSound, cars[i].TrainCar, false);
        }

        // Set brake params
        for (int i = 0; i < cars.Length; i++)
            SetBrakeParams(parts[i].BrakeData, cars[i].TrainCar);

        // Couple them if marked as coupled
        // - we need to do this back to front otherwise the TrainSet indicies will be wrong!
        for (int i = cars.Length - 1; i >= 0; i--)
            Couple(in parts[i], cars[i].TrainCar, autoCouple);

        // Update speed queue data
        for (int i = 0; i < cars.Length; i++)
            cars[i].Client_trainSpeedQueue.ReceiveSnapshot(parts[i].Speed, NetworkLifecycle.Instance.Tick);
    }

    private static NetworkedTrainCar SpawnCar(TrainsetSpawnPart spawnPart, bool preventCoupling = false)
    {
        if (!NetworkedRailTrack.TryGet(spawnPart.Bogie1.TrackNetId, out NetworkedRailTrack bogie1Track) && spawnPart.Bogie1.TrackNetId != 0)
        {
            NetworkLifecycle.Instance.Client.LogDebug(() => $"Tried spawning car but couldn't find track with index {spawnPart.Bogie1.TrackNetId}");
            return null;
        }

        if (!NetworkedRailTrack.TryGet(spawnPart.Bogie2.TrackNetId, out NetworkedRailTrack bogie2Track) && spawnPart.Bogie2.TrackNetId != 0)
        {
            NetworkLifecycle.Instance.Client.LogDebug(() => $"Tried spawning car but couldn't find track with index {spawnPart.Bogie2.TrackNetId}");
            return null;
        }

        if (!TrainComponentLookup.Instance.LiveryFromId(spawnPart.LiveryId, out TrainCarLivery livery))
        {
            NetworkLifecycle.Instance.Client.LogDebug(() => $"Tried spawning car but couldn't find TrainCarLivery with ID {spawnPart.LiveryId}");
            return null;
        }

        //TrainCar trainCar = CarSpawner.Instance.BaseSpawn(livery.prefab, spawnPart.PlayerSpawnedCar, false); //todo: do we need to set the unique flag ever on a client?
        TrainCar trainCar = (CarSpawner.Instance.useCarPooling ? CarSpawner.Instance.GetFromPool(livery.prefab) : UnityEngine.Object.Instantiate(livery.prefab)).GetComponentInChildren<TrainCar>();
        //Multiplayer.LogDebug(() => $"SpawnCar({spawnPart.CarId}) activePrefab: {livery.prefab.activeSelf} activeInstance: {trainCar.gameObject.activeSelf}");
        trainCar.playerSpawnedCar = spawnPart.PlayerSpawnedCar;
        trainCar.uniqueCar = false;
        trainCar.InitializeExistingLogicCar(spawnPart.CarId, spawnPart.CarGuid);

        //set health data
        if (spawnPart.Exploded)
        {
            var explosionBase = trainCar.GetComponent<ResourceExplosionBase>();
            if (explosionBase != null)
                explosionBase.UpdateToExplodedStateExternal();
            else
                TrainCarExplosion.UpdateModelToExploded(trainCar);
        }

        spawnPart.CarHealthData.LoadTo(trainCar);

        // If restoration loco, store the restoration data for processing after all trainsets are spawned
        if (spawnPart.RestorationType != RestorationType.None)
        {
            _restorationData.Add(new RestorationData
            {
                NetId = spawnPart.NetId,
                RestorationState = spawnPart.RestorationState,
                SecondCarNetId = spawnPart.SecondCarNetId
            });
        }

        if (trainCar.PaintExterior != null && spawnPart.PaintExterior != null)
            trainCar.PaintExterior.currentTheme = spawnPart.PaintExterior;

        if (trainCar.PaintInterior != null && spawnPart.PaintInterior != null)
            trainCar.PaintInterior.currentTheme = spawnPart.PaintInterior;

        //Add networked components
        NetworkedTrainCar networkedTrainCar = trainCar.gameObject.GetOrAddComponent<NetworkedTrainCar>();
        networkedTrainCar.NetId = spawnPart.NetId;

        //Setup positions and bogies
        Transform trainTransform = trainCar.transform;
        trainTransform.position = spawnPart.Position + WorldMover.currentMove;
        trainTransform.rotation = spawnPart.Rotation;

        //Multiplayer.LogDebug(() => $"SpawnCar({spawnPart.CarId}) Bogie1 derailed: {spawnPart.Bogie1.HasDerailed}, Rail Track: {bogie1Track?.RailTrack?.name}, Position along track: {spawnPart.Bogie1.PositionAlongTrack}, Track direction: {spawnPart.Bogie1.TrackDirection}, " +
        //    $"Bogie2 derailed: {spawnPart.Bogie2.HasDerailed}, Rail Track: {bogie2Track?.RailTrack?.name}, Position along track: {spawnPart.Bogie2.PositionAlongTrack}, Track direction: {spawnPart.Bogie2.TrackDirection}"
        //);

        if (!spawnPart.Bogie1.HasDerailed)
            trainCar.Bogies[0].SetTrack(bogie1Track.RailTrack, spawnPart.Bogie1.PositionAlongTrack, spawnPart.Bogie1.TrackDirection);
        else
            trainCar.Bogies[0].SetDerailedOnLoadFlag(true);

        if (!spawnPart.Bogie2.HasDerailed)
            trainCar.Bogies[1].SetTrack(bogie2Track.RailTrack, spawnPart.Bogie2.PositionAlongTrack, spawnPart.Bogie2.TrackDirection);
        else
            trainCar.Bogies[1].SetDerailedOnLoadFlag(true);

        trainCar.TryAddFastTravelDestination();

        CarSpawner.Instance.FireCarSpawned(trainCar);

        return networkedTrainCar;
    }

    private static void Couple(in TrainsetSpawnPart spawnPart, TrainCar trainCar, bool autoCouple)
    {
        TrainsetSpawnPart sp = spawnPart;
        //Multiplayer.LogDebug(() =>$"Couple([{sp.CarId}, {sp.NetId}], trainCar, {autoCouple})");

        if (autoCouple)
        {
            trainCar.frontCoupler.preventAutoCouple = spawnPart.FrontCoupling.PreventAutoCouple;
            trainCar.rearCoupler.preventAutoCouple = spawnPart.RearCoupling.PreventAutoCouple;

            trainCar.frontCoupler.AttemptAutoCouple();
            trainCar.rearCoupler.AttemptAutoCouple();

            return;
        }

        //Handle coupling at front of car
        HandleCoupling(spawnPart.FrontCoupling, trainCar.frontCoupler);

        //Handle coupling at rear of car
        HandleCoupling(spawnPart.RearCoupling, trainCar.rearCoupler);
    }

    private static void HandleCoupling(CouplingData couplingData, Coupler currentCoupler)
    {

        CouplingData cd = couplingData;
        TrainCar tc = currentCoupler.train;
        var net = tc.GetNetId();

        //Multiplayer.LogDebug(() => $"HandleCoupling([{tc?.ID}, {net}]) couplingData: is front: {currentCoupler.isFrontCoupler}, {couplingData.HoseConnected}, {couplingData.CockOpen}");

        if (couplingData.IsCoupled)
        {
            if (!NetworkedTrainCar.TryGet(couplingData.ConnectionNetId, out TrainCar otherCar))
            {
                Multiplayer.LogWarning($"HandleCoupling([{currentCoupler?.train?.ID}, {currentCoupler?.train?.GetNetId()}]) did not find car at {(currentCoupler.isFrontCoupler ? "Front" : "Rear")} car with netId: {couplingData.ConnectionNetId}");
            }
            else
            {
                var otherCoupler = couplingData.ConnectionToFront ? otherCar.frontCoupler : otherCar.rearCoupler;
                SetCouplingState(currentCoupler, otherCoupler, couplingData.State);
            }
        }

        CarsSaveManager.RestoreHoseAndCock(currentCoupler, couplingData.HoseConnected, couplingData.CockOpen);
    }

    public static void SetCouplingState(Coupler coupler, Coupler otherCoupler, ChainCouplerInteraction.State targetState)
    {
        //Multiplayer.LogDebug(() => $"SetCouplingState({coupler.train.ID}, {otherCoupler.train.ID}, {targetState}) Coupled: {coupler.IsCoupled()}");

        if (coupler.IsCoupled() && targetState == ChainCouplerInteraction.State.Attached_Tight)
        {
            //Multiplayer.LogDebug(() => $"SetCouplingState({coupler.train.ID}, {otherCoupler.train.ID}, {targetState}) Coupled, attaching tight");
            coupler.state = ChainCouplerInteraction.State.Parked;
            return;
        }

        coupler.state = targetState;
        if (coupler.state == ChainCouplerInteraction.State.Attached_Tight)
        {
            //Multiplayer.LogDebug(() => $"SetCouplingState({coupler.train.ID}, {otherCoupler.train.ID}, {targetState}) Not coupled, attaching tight");
            coupler.CoupleTo(otherCoupler, false);
            coupler.SetChainTight(true);
        }
        else if (coupler.state == ChainCouplerInteraction.State.Attached_Loose)
        {
            //Multiplayer.LogDebug(() => $"SetCouplingState({coupler.train.ID}, {otherCoupler.train.ID}, {targetState}) Unknown coupled, attaching loose");
            coupler.CoupleTo(otherCoupler, false);
            coupler.SetChainTight(false);
        }

        if (!coupler.IsCoupled())
        {
            //Multiplayer.LogDebug(() => $"SetCouplingState({coupler.train.ID}, {otherCoupler.train.ID}, {targetState}) Failed to couple, activating buffer collider");
            coupler.fakeBuffersCollider.enabled = true;
        }

    }

    private static void SetBrakeParams(BrakeSystemData brakeSystemData, TrainCar trainCar)
    {
        BrakeSystem bs = trainCar.brakeSystem;

        if (bs == null)
        {
            Multiplayer.LogWarning($"NetworkedCarSpawner.SetBrakeParams() Brake system is null! netId: {trainCar?.GetNetId()}, trainCar: {trainCar?.ID}");
            return;
        }

        if (bs.hasHandbrake)
            bs.SetHandbrakePosition(brakeSystemData.HandBrakePosition);
        if (bs.hasTrainBrake)
            bs.trainBrakePosition = brakeSystemData.TrainBrakePosition;

        bs.SetBrakePipePressure(brakeSystemData.BrakePipePressure);
        bs.SetAuxReservoirPressure(brakeSystemData.AuxResPressure);
        bs.SetMainReservoirPressure(brakeSystemData.MainResPressure);
        bs.SetControlReservoirPressure(brakeSystemData.ControlResPressure);
        bs.ForceCylinderPressure(brakeSystemData.BrakeCylPressure);

    }

    public static void ApplyRestorationStates()
    {
        Multiplayer.LogDebug(() => $"Applying restoration states for {_restorationData.Count} cars");

        foreach (var data in _restorationData)
        {
            JObject restorationData = [];
            LocoRestorationController.RestorationState state = data.RestorationState;

            // Temporarily disable parts ordered state until we have a better way to sync the ordering process
            if (state == LocoRestorationController.RestorationState.S6_PartPickedUp)
                state = LocoRestorationController.RestorationState.S5_PartOrdered;

            restorationData.SetInt(LocoRestorationController.STATE_SAVE_KEY, (int)state);

            // Find the TrainCar and retrieve GUID
            if (!NetworkedTrainCar.TryGet(data.NetId, out TrainCar trainCar))
            {
                Multiplayer.LogWarning($"Unable to apply restoration state for TrainCar with netId: {data.NetId}, could not find NetworkedTrainCar");
                continue;
            }

            restorationData.SetString(LocoRestorationController.LOCO_GUID_SAVE_KEY, trainCar.CarGUID);

            // If loco has tender, find the second car and retrieve its GUID
            if (data.SecondCarNetId != 0)
            {
                if (!NetworkedTrainCar.TryGet(data.SecondCarNetId, out TrainCar secondCar))
                {
                    Multiplayer.LogWarning($"Unable to apply restoration state for TrainCar with netId: {data.NetId} could not find second car with netId: {data.SecondCarNetId}");
                    continue;
                }
                restorationData.SetString(LocoRestorationController.SECOND_CAR_GUID_SAVE_KEY, secondCar.CarGUID);
            }

            // Apply the restoration state to the loco's LocoRestorationController
            var restorationController = LocoRestorationController.allLocoRestorationControllers.Where(r => r.SaveID == trainCar.carLivery.id).FirstOrDefault();
            if (restorationController != null)
            {
                var locoBlocker = trainCar.GetComponentInChildren<LocoZoneBlocker>(true);
                var prefabBlocker = restorationController.locoBlockerPrefab;
                var secondCarBlocker = restorationController.secondCarBlockerPrefab;

                var liveryBlocker = restorationController.locoLivery.prefab.GetComponentInChildren<LocoZoneBlocker>(true);

                Multiplayer.LogDebug(() => $"Found LocoRestorationController for TrainCar with netId: {data.NetId}, SaveID: {restorationController.SaveID}, Car has blocker: {locoBlocker != null}, prefabBlocker: {prefabBlocker != null}, secondCarBlocker: {secondCarBlocker != null}, liveryBlocker: {liveryBlocker != null}");

                if (locoBlocker == null && prefabBlocker == null && liveryBlocker != null)
                {
                    Multiplayer.LogDebug(() => $"Adding blocker to LocoRestorationController");
                    restorationController.locoBlockerPrefab = liveryBlocker.gameObject;
                }

                Multiplayer.LogDebug(() => $"Applying restoration state for TrainCar with netId: {data.NetId}, restoration state: {state}");
                restorationController.LoadData(restorationData);
            }
            else
            {
                Multiplayer.LogWarning($"Unable to apply restoration state for TrainCar with netId: {data.NetId} could not find LocoRestorationController");
            }
        }

        Multiplayer.LogDebug(() => $"Finished applying restoration states");
    }
}
