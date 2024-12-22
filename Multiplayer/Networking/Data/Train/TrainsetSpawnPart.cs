using DV.ThingTypes;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Serialization;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Multiplayer.Networking.Data.Train;

public readonly struct TrainsetSpawnPart
{
    private static readonly byte[] EMPTY_GUID = new Guid().ToByteArray(); // Empty GUID as bytes

    public readonly ushort NetId;

    //car details
    public readonly string LiveryId;
    public readonly string CarId;
    public readonly string CarGuid;

    //Cargo details
    public readonly CargoType CargoType;
    public readonly float LoadedAmount;

    //customisation details
    public readonly bool PlayerSpawnedCar;

    //coupling details
    public readonly ushort FrontConnectionNetId;    //if we are coupled or hosed this will be the netId of the other car
    public readonly bool FrontConnectionToFront;    //if we are coupled or hosed this will be 'true' if connected to the front other car
    public readonly bool IsFrontCoupled;
    public readonly ChainCouplerInteraction.State FrontState;
    public readonly bool FrontHoseConnected;
    public readonly bool PreventFrontAutoCouple;

    public readonly ushort RearConnectionNetId;    //if we are coupled or hosed this will be the netId of the other car
    public readonly bool RearConnectionToFront;    //if we are coupled or hosed this will be 'true' if connected to the front other car
    public readonly bool IsRearCoupled;
    public readonly ChainCouplerInteraction.State RearState;
    public readonly bool RearHoseConnected;
    public readonly bool PreventRearAutoCouple;

    //positional details
    public readonly float Speed;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;

    //bogie details
    public readonly BogieData Bogie1;
    public readonly BogieData Bogie2;

    //brake initial states
    public readonly bool HasHandbrake;
    public readonly bool HasTrainbrake;
    public readonly float HandBrakePosition;
    public readonly float TrainBrakePosition;
    public readonly float BrakePipePressure;
    public readonly float AuxResPressure;
    public readonly float MainResPressure;
    public readonly float ControlResPressure;
    public readonly float BrakeCylPressure;

    public readonly bool FrontCockOpen;
    public readonly bool RearCockOpen;

    private TrainsetSpawnPart(ushort netId, string liveryId, string carId, string carGuid, bool playerSpawnedCar,
                              bool isFrontCoupled, ChainCouplerInteraction.State frontState, ushort frontConnectionNetId, bool frontConnectedToFront, bool preventFrontAutoCouple,
                              bool isRearCoupled, ChainCouplerInteraction.State rearState, ushort rearConnectionNetId, bool rearConnectedToFront, bool preventRearAutoCouple,
                              float speed, Vector3 position, Quaternion rotation,
                              BogieData bogie1, BogieData bogie2,
                              float? handBrakePos, float? trainBrakePos, float brakePipePress, float auxResPress, float mainResPress, float controlResPress, float brakeCylPress,
                              bool frontHoseConnected,
                              bool rearHoseConnected,
                              bool frontCockOpen, bool rearCockOpen)
    {
        NetId = netId;

        LiveryId = liveryId;
        CarId = carId;
        CarGuid = carGuid;

        PlayerSpawnedCar = playerSpawnedCar;

        IsFrontCoupled = isFrontCoupled;
        FrontState = frontState;
        FrontConnectionNetId = frontConnectionNetId;
        FrontConnectionToFront = frontConnectedToFront;
        FrontHoseConnected = frontHoseConnected;
        PreventFrontAutoCouple = preventFrontAutoCouple;

        IsRearCoupled = isRearCoupled;
        RearState = rearState;
        RearConnectionNetId = rearConnectionNetId;
        RearConnectionToFront = rearConnectedToFront;
        RearHoseConnected = rearHoseConnected;
        PreventRearAutoCouple = preventRearAutoCouple;


        Speed = speed;
        Position = position;
        Rotation = rotation;

        Bogie1 = bogie1;
        Bogie2 = bogie2;

        HasHandbrake = handBrakePos != null;
        HasTrainbrake = trainBrakePos != null;

        if (HasHandbrake)
            HandBrakePosition = (float)handBrakePos;

        if (HasTrainbrake)
            TrainBrakePosition = (float)trainBrakePos;

        BrakePipePressure = brakePipePress;
        AuxResPressure = auxResPress;
        MainResPressure = mainResPress;
        ControlResPressure = controlResPress;
        BrakeCylPressure = brakeCylPress;

        FrontCockOpen = frontCockOpen;
        RearCockOpen = rearCockOpen;
    }

    public static void Serialize(NetDataWriter writer, TrainsetSpawnPart data)
    {
        writer.Put(data.NetId);

        writer.Put(data.LiveryId);
        writer.Put(data.CarId);

        //encode our Guid to save 50% bytes in the packet size
        if (Guid.TryParse(data.CarGuid, out Guid guid))
            writer.PutBytesWithLength(guid.ToByteArray());
        else
        {
            Multiplayer.LogError($"TrainsetSpawnPart.TrainsetSpawnPart() failed to parse carGuid: {data.CarGuid}");
            writer.PutBytesWithLength(EMPTY_GUID);
        }

        writer.Put(data.PlayerSpawnedCar);

        writer.Put(data.IsFrontCoupled);
        writer.Put(data.FrontHoseConnected);
        writer.Put((byte)data.FrontState);
        if (data.IsFrontCoupled || data.FrontHoseConnected)
        {
            writer.Put(data.FrontConnectionNetId);
            writer.Put(data.FrontConnectionToFront);
        }
        writer.Put(data.PreventFrontAutoCouple);

        writer.Put(data.IsRearCoupled);
        writer.Put(data.RearHoseConnected);
        writer.Put((byte)data.RearState);
        if (data.IsRearCoupled || data.RearHoseConnected)
        {
            writer.Put(data.RearConnectionNetId);
            writer.Put(data.RearConnectionToFront);
        }
        writer.Put(data.PreventRearAutoCouple);

        writer.Put(data.Speed);
        Vector3Serializer.Serialize(writer, data.Position);
        QuaternionSerializer.Serialize(writer, data.Rotation);

        BogieData.Serialize(writer, data.Bogie1);
        BogieData.Serialize(writer, data.Bogie2);

        writer.Put(data.HasHandbrake);
        if (data.HasHandbrake)
            writer.Put(data.HandBrakePosition);

        writer.Put(data.HasTrainbrake);
        if (data.HasTrainbrake)
            writer.Put(data.TrainBrakePosition);

        writer.Put(data.BrakePipePressure);
        writer.Put(data.AuxResPressure);
        writer.Put(data.MainResPressure);
        writer.Put(data.ControlResPressure);
        writer.Put(data.BrakeCylPressure);

        writer.Put(data.FrontCockOpen);
        writer.Put(data.RearCockOpen);
    }

    public static TrainsetSpawnPart Deserialize(NetDataReader reader)
    {
        ushort netId = reader.GetUShort();                  //NetId

        string liveryId = reader.GetString();               //LiveryId
        string carId = reader.GetString();                  //CarId
        byte[] guidBytes = reader.GetBytesWithLength();     //GuiId

        string carGuid = new Guid(guidBytes).ToString();    //decode GuiId

        bool playerSpawnedCar = reader.GetBool();           //PlayerSpawnedCar

        bool isFrontCoupled = reader.GetBool();             //IsFrontCoupled
        bool isFrontHoseConnected = reader.GetBool();       //IsFrontHose
        ChainCouplerInteraction.State frontState = (ChainCouplerInteraction.State)reader.GetByte();

        ushort frontConnectedToNetId = 0;
        bool frontConnectedToFront = false;
        if (isFrontCoupled || isFrontHoseConnected)
        {
            frontConnectedToNetId = reader.GetUShort();
            frontConnectedToFront = reader.GetBool();
        }
        bool preventFrontAutoCouple = reader.GetBool();

        bool isRearCoupled = reader.GetBool();               //IsRearCoupled
        bool isRearHoseConnected = reader.GetBool();         //IsRearHose
        ChainCouplerInteraction.State rearState = (ChainCouplerInteraction.State)reader.GetByte();
        ushort rearConnectedToNetId = 0;
        bool rearConnectedToFront = false;
        if (isRearCoupled || isRearHoseConnected)
        {
            rearConnectedToNetId = reader.GetUShort();
            rearConnectedToFront = reader.GetBool();
        }
        bool preventRearAutoCouple = reader.GetBool();

        return new TrainsetSpawnPart(
            netId,

            liveryId,
            carId,
            carGuid,

            playerSpawnedCar,

            isFrontCoupled,
            frontState,
            frontConnectedToNetId,
            frontConnectedToFront,
            preventFrontAutoCouple,

            isRearCoupled,
            rearState,
            rearConnectedToNetId,
            rearConnectedToFront,
            preventRearAutoCouple,

            reader.GetFloat(),                              //Speed
            Vector3Serializer.Deserialize(reader),          //Position
            QuaternionSerializer.Deserialize(reader),       //Rotation

            BogieData.Deserialize(reader),                  //Bogie 1
            BogieData.Deserialize(reader),                  //Bogie 2

            reader.GetBool() ? reader.GetFloat() : null,        //HandbrakePos
            reader.GetBool() ? reader.GetFloat() : null,        //TrainBrakePos
            reader.GetFloat(),                              //BrakePipePressure
            reader.GetFloat(),                              //AuxResPressure
            reader.GetFloat(),                              //MainResPressure
            reader.GetFloat(),                              //ControlResPressure
            reader.GetFloat(),                              //BrakeCylPressure

            isFrontHoseConnected,                           //FrontHoseConnected
            isRearHoseConnected,                            //RearHoseConnected

            reader.GetBool(),                               //FrontCockOpen
            reader.GetBool()                                //RearCockOpen
        );
    }

    public static TrainsetSpawnPart FromTrainCar(NetworkedTrainCar networkedTrainCar)
    {
        TrainCar trainCar = networkedTrainCar.TrainCar;
        Transform transform = networkedTrainCar.transform;

        ushort frontConnectedTo = 0;
        bool frontConnectedToFront = false;
        ChainCouplerInteraction.State frontCouplerState = ChainCouplerInteraction.State.Parked;

        ushort rearConnectedTo = 0;
        bool rearConnectedToFront = false;
        ChainCouplerInteraction.State rearCouplerState = ChainCouplerInteraction.State.Parked;

        bool frontCouplerIsCoupled = false;
        bool preventFrontAutoCouple = false;
        bool rearCouplerIsCoupled = false;
        bool preventRearAutoCouple = false;

        bool frontHoseConnected = false;
        bool rearHoseConnected = false;

        bool frontCockOpen = false;
        bool rearCockOpen = false;


        NetworkLifecycle.Instance.Server.LogDebug(() =>
        {
            return $"TrainsetSpawnPart.FromTrainCar({networkedTrainCar?.NetId}) TrainCarID: {trainCar?.ID}, LiveryID: {trainCar?.carLivery?.id}, " +
                   $"Front[Coupled:{trainCar?.frontCoupler?.IsCoupled()}, State:{trainCar?.frontCoupler?.state}, Hose:{trainCar?.frontCoupler?.hoseAndCock?.IsHoseConnected}, Cock:{trainCar?.frontCoupler?.IsCockOpen}], " +
                   $"Rear[Coupled:{trainCar?.rearCoupler?.IsCoupled()}, State:{trainCar?.rearCoupler?.state}, Hose:{trainCar?.rearCoupler?.hoseAndCock?.IsHoseConnected}, Cock:{trainCar?.rearCoupler?.IsCockOpen}]";
        });


        if (trainCar.frontCoupler.IsCoupled())
        {
            Multiplayer.LogDebug(() => $"FromTrainCar([{networkedTrainCar?.NetId},{networkedTrainCar?.TrainCar?.ID}]) front is coupled to netID: {trainCar?.frontCoupler?.coupledTo?.train?.GetNetId()}");
            frontConnectedTo = trainCar.frontCoupler.coupledTo.train.GetNetId();
            frontConnectedToFront = trainCar.frontCoupler.coupledTo.isFrontCoupler;
        }
        else if (trainCar.frontCoupler.hoseAndCock.IsHoseConnected)
        {
            Multiplayer.LogDebug(() => $"FromTrainCar([{networkedTrainCar?.NetId},{networkedTrainCar?.TrainCar?.ID}]) front hose connected to netID: {trainCar?.frontCoupler?.coupledTo?.train?.GetNetId()}");
            frontConnectedTo = trainCar.frontCoupler.GetAirHoseConnectedTo().train.GetNetId();
            frontConnectedToFront = trainCar.frontCoupler.GetAirHoseConnectedTo().isFrontCoupler;
        }

        if (trainCar.rearCoupler.IsCoupled())
        {
            Multiplayer.LogDebug(() => $"FromTrainCar([{networkedTrainCar?.NetId},{networkedTrainCar?.TrainCar?.ID}]) rear is coupled to netID: {trainCar?.rearCoupler?.coupledTo?.train?.GetNetId()}");
            rearConnectedTo = trainCar.rearCoupler.coupledTo.train.GetNetId();
            rearConnectedToFront = trainCar.rearCoupler.coupledTo.isFrontCoupler;
        }
        else if (trainCar.rearCoupler.hoseAndCock.IsHoseConnected)
        {
            Multiplayer.LogDebug(() => $"FromTrainCar([{networkedTrainCar?.NetId},{networkedTrainCar?.TrainCar?.ID}]) rear hose connected to netID: {trainCar?.rearCoupler?.coupledTo?.train?.GetNetId()}");
            rearConnectedTo = trainCar.rearCoupler.GetAirHoseConnectedTo().train.GetNetId();
            rearConnectedToFront = trainCar.rearCoupler.GetAirHoseConnectedTo().isFrontCoupler;
        }

        frontCouplerIsCoupled = trainCar.frontCoupler.IsCoupled();
        preventFrontAutoCouple = trainCar.frontCoupler.preventAutoCouple;
        rearCouplerIsCoupled = trainCar.rearCoupler.IsCoupled();
        preventRearAutoCouple = trainCar.rearCoupler.preventAutoCouple;

        frontCouplerState = trainCar.frontCoupler.state;
        rearCouplerState = trainCar.rearCoupler.state;

        frontHoseConnected = trainCar.frontCoupler.hoseAndCock.IsHoseConnected;
        rearHoseConnected = trainCar.rearCoupler.hoseAndCock.IsHoseConnected;

        frontCockOpen = trainCar.frontCoupler.IsCockOpen;
        rearCockOpen = trainCar.rearCoupler.IsCockOpen;

        return new TrainsetSpawnPart(
            networkedTrainCar.NetId,

            trainCar.carLivery.id,
            trainCar.ID,
            trainCar.CarGUID,

            trainCar.playerSpawnedCar,

            frontCouplerIsCoupled,
            frontCouplerState,
            frontConnectedTo,
            frontConnectedToFront,
            preventFrontAutoCouple,

            rearCouplerIsCoupled,
            rearCouplerState,
            rearConnectedTo,
            rearConnectedToFront,
            preventRearAutoCouple,

            trainCar.GetForwardSpeed(),
            transform.position - WorldMover.currentMove,
            transform.rotation,

            BogieData.FromBogie(trainCar.Bogies[0], true),
            BogieData.FromBogie(trainCar.Bogies[1], true),

            trainCar.brakeSystem.hasHandbrake ? trainCar.brakeSystem.handbrakePosition : null,
            trainCar.brakeSystem.hasTrainBrake ? trainCar.brakeSystem.trainBrakePosition : null,
            trainCar.brakeSystem.brakePipePressure,
            trainCar.brakeSystem.auxReservoirPressure,
            trainCar.brakeSystem.mainReservoirPressure,
            trainCar.brakeSystem.controlReservoirPressure,
            trainCar.brakeSystem.brakeCylinderPressure,

            frontHoseConnected,
            rearHoseConnected,
            frontCockOpen,
            rearCockOpen
        );
    }

    //public static TrainsetSpawnPart[] FromTrainSet(Trainset trainset)
    //{
    //    if (trainset == null)
    //    {
    //        NetworkLifecycle.Instance.Server.LogWarning("TrainsetSpawnPart.FromTrainSet() trainset is null!");
    //        return null;
    //    }

    //    TrainsetSpawnPart[] parts = new TrainsetSpawnPart[trainset.cars.Count];
    //    for (int i = 0; i < trainset.cars.Count; i++)
    //    {
    //        NetworkedTrainCar networkedTrainCar;

    //        if (!trainset.cars[i].TryNetworked(out networkedTrainCar))
    //        {
    //            NetworkLifecycle.Instance.Server.LogWarning($"TrainsetSpawnPart.FromTrainSet({trainset?.id}) Failed to find NetworkedTrainCar for: {trainset?.cars[i]?.ID}");
    //            networkedTrainCar = trainset.cars[i].GetOrAddComponent<NetworkedTrainCar>();
    //        }

    //        parts[i] = FromTrainCar(networkedTrainCar);
    //    }
    //    return parts;
    //}

    public static TrainsetSpawnPart[] FromTrainSet(List<TrainCar> trainset/*, bool resolveCoupling = false*/)
    {
        if (trainset == null)
        {
            NetworkLifecycle.Instance.Server.LogWarning("TrainsetSpawnPart.FromTrainSet() trainset list is null!");
            return null;
        }

        TrainsetSpawnPart[] parts = new TrainsetSpawnPart[trainset.Count];
        for (int i = 0; i < trainset.Count; i++)
        {
            NetworkedTrainCar networkedTrainCar;

            if (!trainset[i].TryNetworked(out networkedTrainCar))
            {
                NetworkLifecycle.Instance.Server.LogWarning($"TrainsetSpawnPart.FromTrainSet() Failed to find NetworkedTrainCar for: {trainset[i]?.ID}");
                networkedTrainCar = trainset[i].GetOrAddComponent<NetworkedTrainCar>();
            }

            parts[i] = FromTrainCar(networkedTrainCar);
        }

        return parts;
    }

}
