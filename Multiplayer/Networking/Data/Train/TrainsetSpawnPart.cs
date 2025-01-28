using DV.Customization.Paint;
using DV.LocoRestoration;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Serialization;
using Multiplayer.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Networking.Data.Train;

public readonly struct TrainsetSpawnPart
{
    private static readonly byte[] EMPTY_GUID = new Guid().ToByteArray(); // Empty GUID as bytes

    public readonly ushort NetId;

    // Car details
    public readonly string LiveryId;
    public readonly string CarId;
    public readonly string CarGuid;
    public readonly bool Exploded;
    public readonly TrainCarHealthData CarHealthData;

    // Customisation details
    public readonly bool PlayerSpawnedCar;
    public readonly bool IsRestorationLoco;
    public readonly LocoRestorationController.RestorationState RestorationState;
    public readonly PaintTheme PaintExterior;
    public readonly PaintTheme PaintInterior;

    // Coupling data
    public readonly CouplingData FrontCoupling;
    public readonly CouplingData RearCoupling;

    // Positional details
    public readonly float Speed;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;

    // Bogie data
    public readonly BogieData Bogie1;
    public readonly BogieData Bogie2;

    // Brake initial states
    public readonly BrakeSystemData BrakeData;

    public TrainsetSpawnPart(
          ushort netId, string liveryId, string carId, string carGuid, bool exploded, TrainCarHealthData carHealthData,
          bool playerSpawnedCar, bool isRestoration, LocoRestorationController.RestorationState restorationState, PaintTheme paintExterior, PaintTheme paintInterior,
          CouplingData frontCoupling, CouplingData rearCoupling,
          float speed, Vector3 position, Quaternion rotation,
          BogieData bogie1, BogieData bogie2, BrakeSystemData brakeData)
    {
        NetId = netId;
        LiveryId = liveryId;
        CarId = carId;
        CarGuid = carGuid;
        Exploded = exploded;
        CarHealthData = carHealthData;

        PlayerSpawnedCar = playerSpawnedCar;
        IsRestorationLoco = isRestoration;
        RestorationState = restorationState;

        PaintExterior = paintExterior;
        PaintInterior = paintInterior;

        FrontCoupling = frontCoupling;
        RearCoupling = rearCoupling;

        Speed = speed;
        Position = position;
        Rotation = rotation;
        Bogie1 = bogie1;
        Bogie2 = bogie2;
        BrakeData = brakeData;
    }

    public static void Serialize(NetDataWriter writer, TrainsetSpawnPart data)
    {
        writer.Put(data.NetId);
        writer.Put(data.LiveryId);
        writer.Put(data.CarId);

        if (Guid.TryParse(data.CarGuid, out Guid guid))
            writer.PutBytesWithLength(guid.ToByteArray());
        else
        {
            Multiplayer.LogError($"TrainsetSpawnPart.Serialize() failed to parse carGuid: {data.CarGuid}");
            writer.PutBytesWithLength(EMPTY_GUID);
        }

        writer.Put(data.Exploded);
        TrainCarHealthData.Serialize(writer, data.CarHealthData);

        writer.Put(data.PlayerSpawnedCar);
        writer.Put(data.IsRestorationLoco);

        if(data.IsRestorationLoco)
            writer.Put((byte) data.RestorationState);

        writer.Put(PaintThemeLookup.Instance.GetThemeIndex(data.PaintExterior));
        writer.Put(PaintThemeLookup.Instance.GetThemeIndex(data.PaintInterior));


        CouplingData.Serialize(writer, data.FrontCoupling);
        CouplingData.Serialize(writer, data.RearCoupling);

        writer.Put(data.Speed);
        Vector3Serializer.Serialize(writer, data.Position);
        QuaternionSerializer.Serialize(writer, data.Rotation);

        BogieData.Serialize(writer, data.Bogie1);
        BogieData.Serialize(writer, data.Bogie2);
        BrakeSystemData.Serialize(writer, data.BrakeData);
    }

    public static TrainsetSpawnPart Deserialize(NetDataReader reader)
    {
        ushort netId = reader.GetUShort();
        string liveryId = reader.GetString();
        string carId = reader.GetString();
        string carGuid = new Guid(reader.GetBytesWithLength()).ToString();
        bool exploded = reader.GetBool();
        TrainCarHealthData healthData = TrainCarHealthData.Deserialize(reader);

        bool playerSpawnedCar = reader.GetBool();
        bool isRestoration = reader.GetBool();
        LocoRestorationController.RestorationState restorationState = default;
        if (isRestoration)
            restorationState = (LocoRestorationController.RestorationState)reader.GetByte();

        sbyte extThemeIndex = reader.GetSByte();
        sbyte intThemeIndex = reader.GetSByte();


        PaintTheme exteriorPaint = PaintThemeLookup.Instance.GetPaintTheme(extThemeIndex);
        PaintTheme interiorPaint = PaintThemeLookup.Instance.GetPaintTheme(intThemeIndex);

        var frontCoupling = CouplingData.Deserialize(reader);
        var rearCoupling = CouplingData.Deserialize(reader);

        float speed = reader.GetFloat();
        Vector3 position = Vector3Serializer.Deserialize(reader);
        Quaternion rotation = QuaternionSerializer.Deserialize(reader);

        var bogie1 = BogieData.Deserialize(reader);
        var bogie2 = BogieData.Deserialize(reader);
        var brakeSet = BrakeSystemData.Deserialize(reader);

        return new TrainsetSpawnPart(
            netId, liveryId, carId, carGuid, exploded, healthData,
            playerSpawnedCar, isRestoration, restorationState, exteriorPaint, interiorPaint,
            frontCoupling, rearCoupling,
            speed, position, rotation,
            bogie1, bogie2, brakeSet);
    }

    public static TrainsetSpawnPart FromTrainCar(NetworkedTrainCar networkedTrainCar)
    {
        TrainCar trainCar = networkedTrainCar.TrainCar;
        Transform transform = networkedTrainCar.transform;


        LocoRestorationController restorationController = LocoRestorationController.GetForTrainCar(trainCar);
        var restorationState = restorationController?.State ?? default;

        return new TrainsetSpawnPart(
            networkedTrainCar.NetId,
            trainCar.carLivery.id,
            trainCar.ID,
            trainCar.CarGUID,
            trainCar.isExploded,
            TrainCarHealthData.From(trainCar),

            trainCar.playerSpawnedCar,
            restorationController != null,
            restorationState,
            
            trainCar?.PaintExterior?.currentTheme,
            trainCar?.PaintInterior?.currentTheme,

            frontCoupling: CouplingData.From(trainCar.frontCoupler),
            rearCoupling: CouplingData.From(trainCar.rearCoupler),
            trainCar.GetForwardSpeed(),
            transform.position - WorldMover.currentMove,
            transform.rotation,
            BogieData.FromBogie(trainCar.Bogies[0], true),
            BogieData.FromBogie(trainCar.Bogies[1], true),
            BrakeSystemData.From(trainCar.brakeSystem)
        );
    }

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
