using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using System;
using UnityEngine;
namespace Multiplayer.Networking.Data.Train;

public readonly struct TrainsetMovementPart
{
    public readonly MovementType typeFlag;
    public readonly float Speed;
    public readonly float SlowBuildUpStress;
    public readonly Vector3 Position;       //Used in sync only
    public readonly Quaternion Rotation;    //Used in sync only
    public readonly BogieData Bogie1;
    public readonly BogieData Bogie2;
    public readonly RigidbodySnapshot RigidbodySnapshot;

    [Flags]
    public enum MovementType : byte
    {
        Physics = 1,
        RigidBody = 2,
        Position = 4
    }

    public TrainsetMovementPart(float speed, float slowBuildUpStress, BogieData bogie1, BogieData bogie2, Vector3? position = null, Quaternion? rotation = null)
    {
        typeFlag = MovementType.Physics;    //no rigid body data

        Speed = speed;
        SlowBuildUpStress = slowBuildUpStress;
        Bogie1 = bogie1;
        Bogie2 = bogie2;

        if (position != null && rotation != null)
        {
            //Multiplayer.LogDebug(()=>$"new TrainsetMovementPart() Sync");

            typeFlag |= MovementType.Position;  //includes positional data

            Position = (Vector3)position;
            Rotation = (Quaternion)rotation;
        }
    }

    public TrainsetMovementPart(RigidbodySnapshot rigidbodySnapshot)
    {
        typeFlag = MovementType.RigidBody;    //rigid body data

        //Multiplayer.LogDebug(() => $"new TrainsetMovementPart() RigidBody");

        RigidbodySnapshot = rigidbodySnapshot;
    }

#pragma warning disable EPS05 // Use in-modifier for a readonly struct
    public static void Serialize(NetDataWriter writer, TrainsetMovementPart data)
#pragma warning restore EPS05 // Use in-modifier for a readonly struct
    {
        writer.Put((byte)data.typeFlag);

        //Multiplayer.LogDebug(() => $"TrainsetMovementPart.Serialize() {data.typeFlag}");

        if (data.typeFlag.HasFlag(MovementType.RigidBody))
        {
            RigidbodySnapshot.Serialize(writer, data.RigidbodySnapshot);
            return;
        }

        if (data.typeFlag.HasFlag(MovementType.Physics))
        {
            writer.Put(data.Speed);
            writer.Put(data.SlowBuildUpStress);
            BogieData.Serialize(writer, data.Bogie1);
            BogieData.Serialize(writer, data.Bogie2);
        }

        if (data.typeFlag.HasFlag(MovementType.Position))
        {
            Vector3Serializer.Serialize(writer, data.Position);
            QuaternionSerializer.Serialize(writer, data.Rotation);
        }
    }

    public static TrainsetMovementPart Deserialize(NetDataReader reader)
    {
        float speed = 0;
        float slowBuildUpStress = 0;
        Vector3? position = null;
        Quaternion? rotation = null;
        BogieData bd1 = default;
        BogieData bd2 = default;

        MovementType dataType = (MovementType)reader.GetByte();

        if (dataType.HasFlag(MovementType.RigidBody))
        {
            return new TrainsetMovementPart(RigidbodySnapshot.Deserialize(reader));
        }

        if (dataType.HasFlag(MovementType.Physics))
        {
            speed = reader.GetFloat();
            slowBuildUpStress = reader.GetFloat();
            bd1 = BogieData.Deserialize(reader);
            bd2 = BogieData.Deserialize(reader);
        }

        if (dataType.HasFlag(MovementType.Position))
        {
            position = Vector3Serializer.Deserialize(reader);
            rotation = QuaternionSerializer.Deserialize(reader);
        }

        return new TrainsetMovementPart(speed, slowBuildUpStress, bd1, bd2, position, rotation);
    }
}
