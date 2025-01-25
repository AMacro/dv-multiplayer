using LiteNetLib.Utils;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Serialization;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Networking.Data;

public readonly struct TrainsetSpawnPart
{
    public readonly ushort NetId;
    public readonly string LiveryId;
    public readonly string CarId;
    public readonly string CarGuid;
    public readonly bool PlayerSpawnedCar;
    public readonly bool IsFrontCoupled;
    public readonly bool IsRearCoupled;
    public readonly float Speed;
    public readonly Vector3 Position;
    public readonly Vector3 Rotation;
    public readonly BogieData Bogie1;
    public readonly BogieData Bogie2;

    private TrainsetSpawnPart(ushort netId, string liveryId, string carId, string carGuid, bool playerSpawnedCar, bool isFrontCoupled, bool isRearCoupled, float speed, Vector3 position, Vector3 rotation,
        BogieData bogie1, BogieData bogie2)
    {
        NetId = netId;
        LiveryId = liveryId;
        CarId = carId;
        CarGuid = carGuid;
        PlayerSpawnedCar = playerSpawnedCar;
        IsFrontCoupled = isFrontCoupled;
        IsRearCoupled = isRearCoupled;
        Speed = speed;
        Position = position;
        Rotation = rotation;
        Bogie1 = bogie1;
        Bogie2 = bogie2;
    }

    public static void Serialize(NetDataWriter writer, TrainsetSpawnPart data)
    {
        writer.Put(data.NetId);
        writer.Put(data.LiveryId);
        writer.Put(data.CarId);
        writer.Put(data.CarGuid);
        writer.Put(data.PlayerSpawnedCar);
        writer.Put(data.IsFrontCoupled);
        writer.Put(data.IsRearCoupled);
        writer.Put(data.Speed);
        Vector3Serializer.Serialize(writer, data.Position);
        Vector3Serializer.Serialize(writer, data.Rotation);
        BogieData.Serialize(writer, data.Bogie1);
        BogieData.Serialize(writer, data.Bogie2);
    }

    public static TrainsetSpawnPart Deserialize(NetDataReader reader)
    {
        return new TrainsetSpawnPart(
            reader.GetUShort(),
            reader.GetString(),
            reader.GetString(),
            reader.GetString(),
            reader.GetBool(),
            reader.GetBool(),
            reader.GetBool(),
            reader.GetFloat(),
            Vector3Serializer.Deserialize(reader),
            Vector3Serializer.Deserialize(reader),
            BogieData.Deserialize(reader),
            BogieData.Deserialize(reader)
        );
    }

    public static TrainsetSpawnPart FromTrainCar(NetworkedTrainCar networkedTrainCar)
    {
        TrainCar trainCar = networkedTrainCar.TrainCar;
        Transform transform = networkedTrainCar.transform;
        return new TrainsetSpawnPart(
            networkedTrainCar.NetId,
            trainCar.carLivery.id,
            trainCar.ID,
            trainCar.CarGUID,
            trainCar.playerSpawnedCar,
            trainCar.frontCoupler.IsCoupled(),
            trainCar.rearCoupler.IsCoupled(),
            trainCar.GetForwardSpeed(),
            transform.position - WorldMover.currentMove,
            transform.eulerAngles,
            BogieData.FromBogie(trainCar.Bogies[0], true, networkedTrainCar.Bogie1TrackDirection),
            BogieData.FromBogie(trainCar.Bogies[1], true, networkedTrainCar.Bogie2TrackDirection)
        );
    }

    public static TrainsetSpawnPart[] FromTrainSet(Trainset trainset)
    {
        TrainsetSpawnPart[] parts = new TrainsetSpawnPart[trainset.cars.Count];
        for (int i = 0; i < trainset.cars.Count; i++)
            parts[i] = FromTrainCar(trainset.cars[i].Networked());
        return parts;
    }
}
