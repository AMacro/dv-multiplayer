using DV.Simulation.Brake;
using LiteNetLib.Utils;

namespace Multiplayer.Networking.Data.Train;

public readonly struct BrakeSystemData
{
    public readonly bool HasHandbrake;
    public readonly bool HasTrainbrake;
    public readonly float HandBrakePosition;
    public readonly float TrainBrakePosition;
    public readonly float BrakePipePressure;
    public readonly float AuxResPressure;
    public readonly float MainResPressure;
    public readonly float ControlResPressure;
    public readonly float BrakeCylPressure;

    public BrakeSystemData(
        bool hasHandbrake, bool hasTrainbrake,
        float handBrakePosition, float trainBrakePosition,
        float brakePipePressure, float auxResPressure,
        float mainResPressure, float controlResPressure,
        float brakeCylPressure)
    {
        HasHandbrake = hasHandbrake;
        HasTrainbrake = hasTrainbrake;
        HandBrakePosition = handBrakePosition;
        TrainBrakePosition = trainBrakePosition;
        BrakePipePressure = brakePipePressure;
        AuxResPressure = auxResPressure;
        MainResPressure = mainResPressure;
        ControlResPressure = controlResPressure;
        BrakeCylPressure = brakeCylPressure;
    }

    public static void Serialize(NetDataWriter writer, BrakeSystemData data)
    {
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
    }

    public static BrakeSystemData Deserialize(NetDataReader reader)
    {
        bool hasHandbrake = reader.GetBool();
        float handBrakePosition = hasHandbrake ? reader.GetFloat() : 0f;

        bool hasTrainbrake = reader.GetBool();
        float trainBrakePosition = hasTrainbrake ? reader.GetFloat() : 0f;

        return new BrakeSystemData(
            hasHandbrake,
            hasTrainbrake,
            handBrakePosition,
            trainBrakePosition,
            reader.GetFloat(),  // BrakePipePressure
            reader.GetFloat(),  // AuxResPressure
            reader.GetFloat(),  // MainResPressure
            reader.GetFloat(),  // ControlResPressure
            reader.GetFloat()   // BrakeCylPressure
        );
    }

    public static BrakeSystemData From(BrakeSystem brakeSystem)
    {
        return new BrakeSystemData(
            hasHandbrake: brakeSystem.hasHandbrake,
            hasTrainbrake: brakeSystem.hasTrainBrake,
            handBrakePosition: brakeSystem.handbrakePosition,
            trainBrakePosition: brakeSystem.trainBrakePosition,
            brakePipePressure: brakeSystem.brakePipePressure,
            auxResPressure: brakeSystem.auxReservoirPressure,
            mainResPressure: brakeSystem.mainReservoirPressure,
            controlResPressure: brakeSystem.controlReservoirPressure,
            brakeCylPressure: brakeSystem.brakeCylinderPressure
        );
    }

}
