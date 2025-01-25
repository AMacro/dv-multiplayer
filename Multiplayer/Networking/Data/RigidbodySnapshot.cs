using System;
using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Data;

public class RigidbodySnapshot
{
    public byte IncludedDataFlags { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Rotation { get; set; }
    public Vector3 Velocity { get; set; }
    public Vector3 AngularVelocity { get; set; }

    public static void Serialize(NetDataWriter writer, RigidbodySnapshot data)
    {
        writer.Put(data.IncludedDataFlags);
        IncludedData flags = (IncludedData)data.IncludedDataFlags;
        if (flags.HasFlag(IncludedData.Position))
            Vector3Serializer.Serialize(writer, data.Position);
        if (flags.HasFlag(IncludedData.Rotation))
            Vector3Serializer.Serialize(writer, data.Rotation);
        if (flags.HasFlag(IncludedData.Velocity))
            Vector3Serializer.Serialize(writer, data.Velocity);
        if (flags.HasFlag(IncludedData.AngularVelocity))
            Vector3Serializer.Serialize(writer, data.AngularVelocity);
    }

    public static RigidbodySnapshot Deserialize(NetDataReader reader)
    {
        IncludedData IncludedDataFlags = (IncludedData)reader.GetByte();
        RigidbodySnapshot snapshot = new() {
            IncludedDataFlags = (byte)IncludedDataFlags
        };
        if (IncludedDataFlags.HasFlag(IncludedData.Position))
            snapshot.Position = Vector3Serializer.Deserialize(reader);
        if (IncludedDataFlags.HasFlag(IncludedData.Rotation))
            snapshot.Rotation = Vector3Serializer.Deserialize(reader);
        if (IncludedDataFlags.HasFlag(IncludedData.Velocity))
            snapshot.Velocity = Vector3Serializer.Deserialize(reader);
        if (IncludedDataFlags.HasFlag(IncludedData.AngularVelocity))
            snapshot.AngularVelocity = Vector3Serializer.Deserialize(reader);
        return snapshot;
    }

    public static RigidbodySnapshot From(Rigidbody rb, IncludedData includedDataFlags = IncludedData.All)
    {
        RigidbodySnapshot snapshot = new() {
            IncludedDataFlags = (byte)includedDataFlags
        };
        if (includedDataFlags.HasFlag(IncludedData.Position))
            snapshot.Position = rb.position - WorldMover.currentMove;
        if (includedDataFlags.HasFlag(IncludedData.Rotation))
            snapshot.Rotation = rb.rotation.eulerAngles;
        if (includedDataFlags.HasFlag(IncludedData.Velocity))
            snapshot.Velocity = rb.velocity;
        if (includedDataFlags.HasFlag(IncludedData.AngularVelocity))
            snapshot.AngularVelocity = rb.angularVelocity;
        return snapshot;
    }

    public void Apply(Rigidbody rb)
    {
        IncludedData flags = (IncludedData)IncludedDataFlags;
        if (flags.HasFlag(IncludedData.Position))
            rb.MovePosition(Position + WorldMover.currentMove);
        if (flags.HasFlag(IncludedData.Position))
            rb.MoveRotation(Quaternion.Euler(Rotation));
        if (flags.HasFlag(IncludedData.Position))
            rb.velocity = Velocity;
        if (flags.HasFlag(IncludedData.Position))
            rb.angularVelocity = AngularVelocity;
    }

    [Flags]
    public enum IncludedData : byte
    {
        Position = 1,
        Rotation = 2,
        Velocity = 4,
        AngularVelocity = 8,
        All = Position | Rotation | Velocity | AngularVelocity,
        Velocities = Velocity | AngularVelocity
    }
}
