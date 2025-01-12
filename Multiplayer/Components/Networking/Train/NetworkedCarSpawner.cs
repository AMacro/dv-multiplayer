using System.Collections;
using DV.LocoRestoration;
using DV.Simulation.Brake;
using DV.ThingTypes;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Train;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.Train;

public static class NetworkedCarSpawner
{
    public static void SpawnCars(TrainsetSpawnPart[] parts, bool autoCouple)
    {
        NetworkedTrainCar[] cars = new NetworkedTrainCar[parts.Length];

        //spawn the cars
        for (int i = 0; i < parts.Length; i++)
            cars[i] = SpawnCar(parts[i], true);

        //Set brake params
        for (int i = 0; i < cars.Length; i++)
            SetBrakeParams(parts[i].BrakeData, cars[i].TrainCar);

        //couple them if marked as coupled
        for (int i = 0; i < cars.Length; i++)
            Couple(parts[i], cars[i].TrainCar, autoCouple);

        //update speed queue data
        for (int i = 0; i < cars.Length; i++)
            cars[i].Client_trainSpeedQueue.ReceiveSnapshot(parts[i].Speed, NetworkLifecycle.Instance.Tick);
    }

    public static NetworkedTrainCar SpawnCar(TrainsetSpawnPart spawnPart, bool preventCoupling = false)
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

        //TrainCar trainCar = CarSpawner.Instance.BaseSpawn(livery.prefab, spawnPart.PlayerSpawnedCar, false); //todo: do we need to set the unique flag ever on a client?
        TrainCar trainCar = (CarSpawner.Instance.useCarPooling ? CarSpawner.Instance.GetFromPool(livery.prefab) : UnityEngine.Object.Instantiate(livery.prefab)).GetComponentInChildren<TrainCar>();
        //Multiplayer.LogDebug(() => $"SpawnCar({spawnPart.CarId}) activePrefab: {livery.prefab.activeSelf} activeInstance: {trainCar.gameObject.activeSelf}");
        trainCar.playerSpawnedCar = spawnPart.PlayerSpawnedCar;
        trainCar.uniqueCar = false;
        trainCar.InitializeExistingLogicCar(spawnPart.CarId, spawnPart.CarGuid);

        //Restoration vehicle hack
        //todo: make it work properly
        if (spawnPart.IsRestorationLoco)
            switch(spawnPart.RestorationState)
            {
                case LocoRestorationController.RestorationState.S0_Initialized:
                case LocoRestorationController.RestorationState.S1_UnlockedRestorationLicense:
                case LocoRestorationController.RestorationState.S2_LocoUnblocked:
                    BlockLoco(trainCar);

                    break;
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
        Multiplayer.LogDebug(() =>$"Couple([{sp.CarId}, {sp.NetId}], trainCar, {autoCouple})");

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

    private static void HandleCoupling(CouplingData couplingData,  Coupler currentCoupler)
    {

        CouplingData cd = couplingData;
        TrainCar tc = currentCoupler.train;
        var net = tc.GetNetId();

        Multiplayer.LogDebug(() => $"HandleCoupling([{tc?.ID}, {net}]) couplingData: is front: {currentCoupler.isFrontCoupler}, {couplingData.HoseConnected}, {couplingData.CockOpen}");

        if (couplingData.IsCoupled)
        {
            if (!NetworkedTrainCar.GetTrainCar(couplingData.ConnectionNetId, out TrainCar otherCar))
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

        if(bs.hasHandbrake)
            bs.SetHandbrakePosition(brakeSystemData.HandBrakePosition);
        if(bs.hasTrainBrake)
            bs.trainBrakePosition = brakeSystemData.TrainBrakePosition;

        bs.SetBrakePipePressure(brakeSystemData.BrakePipePressure);
        bs.SetAuxReservoirPressure(brakeSystemData.AuxResPressure);
        bs.SetMainReservoirPressure(brakeSystemData.MainResPressure);
        bs.SetControlReservoirPressure(brakeSystemData.ControlResPressure);
        bs.ForceCylinderPressure(brakeSystemData.BrakeCylPressure);

    }

    private static void BlockLoco(TrainCar trainCar)
    {
        trainCar.blockInteriorLoading = true;
        trainCar.preventFastTravelWithCar = true;
        trainCar.preventFastTravelDestination = true;

        if (trainCar.FastTravelDestination != null)
        {
            trainCar.FastTravelDestination.showOnMap = false;
            trainCar.FastTravelDestination.RefreshMarkerVisibility();
        }

        trainCar.preventDebtDisplay = true;
        trainCar.preventRerail = true;
        trainCar.preventDelete = true;
        trainCar.preventService = true;
        trainCar.preventCouple = true;
    }
}
