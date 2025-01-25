using LiteNetLib.Utils;

namespace Multiplayer.Networking.Data;

public readonly struct TrainsetMovementPart
{
    public readonly bool IsRigidbodySnapshot;
    public readonly float Speed;
    public readonly float SlowBuildUpStress;
    public readonly BogieData Bogie1;
    public readonly BogieData Bogie2;
    public readonly RigidbodySnapshot RigidbodySnapshot;

    public TrainsetMovementPart(float speed, float slowBuildUpStress, BogieData bogie1, BogieData bogie2)
    {
        IsRigidbodySnapshot = false;
        Speed = speed;
        SlowBuildUpStress = slowBuildUpStress;
        Bogie1 = bogie1;
        Bogie2 = bogie2;
    }

    public TrainsetMovementPart(RigidbodySnapshot rigidbodySnapshot)
    {
        IsRigidbodySnapshot = true;
        RigidbodySnapshot = rigidbodySnapshot;
    }

#pragma warning disable EPS05
    public static void Serialize(NetDataWriter writer, TrainsetMovementPart data)
#pragma warning restore EPS05
    {
        writer.Put(data.IsRigidbodySnapshot);

        if (data.IsRigidbodySnapshot)
        {
            RigidbodySnapshot.Serialize(writer, data.RigidbodySnapshot);
            return;
        }

        writer.Put(data.Speed);
        writer.Put(data.SlowBuildUpStress);
        BogieData.Serialize(writer, data.Bogie1);
        BogieData.Serialize(writer, data.Bogie2);
    }

    public static TrainsetMovementPart Deserialize(NetDataReader reader)
    {
        bool isRigidbodySnapshot = reader.GetBool();
        return isRigidbodySnapshot
            ? new TrainsetMovementPart(RigidbodySnapshot.Deserialize(reader))
            : new TrainsetMovementPart(
                reader.GetFloat(),
                reader.GetFloat(),
                BogieData.Deserialize(reader),
                BogieData.Deserialize(reader)
            );
    }
}
