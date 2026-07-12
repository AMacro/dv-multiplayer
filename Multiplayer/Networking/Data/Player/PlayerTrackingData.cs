using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using System;
using UnityEngine;

namespace Multiplayer.Networking.Data.Player;

public struct PlayerTrackingData
{
    [Flags]
    private enum DataFlags : uint
    {
        None = 0,
        Position = 1,
        MoveDirection = 2,
        RotationY = 4,

        LookPosition = 8,

        SitHeight = 16,

        LeftHandPosition = 32,
        LeftHandRotation = 64,
        LeftHandClosed = 128,

        RightHandPosition = 256,
        RightHandRotation = 512,
        RightHandClosed = 1024,

        // Todo: add hip and foot tracking in the future
        HipPosition = 2048,
        HipRotation = 4096,

        LeftFootPosition = 8192,
        LeftFootRotation = 16384,

        RightFootPosition = 32768,
        RightFootRotation = 65536,
    }

    private DataFlags Flags
    {
        get
        {
            DataFlags flags = DataFlags.None;
            if (Position.HasValue) flags |= DataFlags.Position;
            if (MoveDirection.HasValue) flags |= DataFlags.MoveDirection;
            if (RotationY.HasValue) flags |= DataFlags.RotationY;

            if (LookPosition.HasValue) flags |= DataFlags.LookPosition;

            if (SitHeight.HasValue) flags |= DataFlags.SitHeight;

            if (LeftHandPosition.HasValue) flags |= DataFlags.LeftHandPosition;
            if (LeftHandRotation.HasValue) flags |= DataFlags.LeftHandRotation;
            if (LeftHandOpen.HasValue && LeftHandOpen == false) flags |= DataFlags.LeftHandClosed;

            if (RightHandPosition.HasValue) flags |= DataFlags.RightHandPosition;
            if (RightHandRotation.HasValue) flags |= DataFlags.RightHandRotation;
            if (RightHandOpen.HasValue && RightHandOpen == false) flags |= DataFlags.RightHandClosed;

            if (HipPosition.HasValue) flags |= DataFlags.HipPosition;
            if (HipRotation.HasValue) flags |= DataFlags.HipRotation;

            if (LeftFootPosition.HasValue) flags |= DataFlags.LeftFootPosition;
            if (LeftFootRotation.HasValue) flags |= DataFlags.LeftFootRotation;

            if (RightFootPosition.HasValue) flags |= DataFlags.RightFootPosition;
            if (RightFootRotation.HasValue) flags |= DataFlags.RightFootRotation;

            return flags;
        }
    }

    public bool HasAdditionalData =>
        LeftHandPosition.HasValue ||
        LeftHandRotation.HasValue ||
        LeftHandOpen.HasValue ||
        RightHandPosition.HasValue ||
        RightHandRotation.HasValue ||
        RightHandOpen.HasValue ||
        HipPosition.HasValue ||
        HipRotation.HasValue ||
        LeftFootPosition.HasValue ||
        LeftFootRotation.HasValue ||
        RightFootPosition.HasValue ||
        RightFootRotation.HasValue;

    public Vector3? Position;
    public Vector2? MoveDirection;
    public float? RotationY;
    public float? LookPosition;
    public float? SitHeight;

    public Vector3? LeftHandPosition;
    public Quaternion? LeftHandRotation;
    public bool? LeftHandOpen;

    public Vector3? RightHandPosition;
    public Quaternion? RightHandRotation;
    public bool? RightHandOpen;

    public Vector3? HipPosition;
    public Quaternion? HipRotation;

    public Vector3? LeftFootPosition;
    public Quaternion? LeftFootRotation;

    public Vector3? RightFootPosition;
    public Quaternion? RightFootRotation;

    public static void Serialize(NetDataWriter writer, PlayerTrackingData data)
    {
        writer.Put((uint)data.Flags);

        if (data.Position.HasValue) Vector3Serializer.Serialize(writer, data.Position.Value);
        if (data.MoveDirection.HasValue) Vector2Serializer.Serialize(writer, data.MoveDirection.Value);
        if (data.RotationY.HasValue) writer.Put(data.RotationY.Value);

        if (data.LookPosition.HasValue) writer.Put(data.LookPosition.Value);

        if (data.SitHeight.HasValue) writer.Put(data.SitHeight.Value);

        if (data.LeftHandPosition.HasValue) Vector3Serializer.Serialize(writer, data.LeftHandPosition.Value);
        if (data.LeftHandRotation.HasValue) QuaternionSerializer.Serialize(writer, data.LeftHandRotation.Value);
        // Left hand open/closed covered by flags, no need to serialise it separately

        if (data.RightHandPosition.HasValue) Vector3Serializer.Serialize(writer, data.RightHandPosition.Value);
        if (data.RightHandRotation.HasValue) QuaternionSerializer.Serialize(writer, data.RightHandRotation.Value);
        // Right hand open/closed covered by flags, no need to serialise it separately

        if (data.HipPosition.HasValue) Vector3Serializer.Serialize(writer, data.HipPosition.Value);
        if (data.HipRotation.HasValue) QuaternionSerializer.Serialize(writer, data.HipRotation.Value);

        if (data.LeftFootPosition.HasValue) Vector3Serializer.Serialize(writer, data.LeftFootPosition.Value);
        if (data.LeftFootRotation.HasValue) QuaternionSerializer.Serialize(writer, data.LeftFootRotation.Value);

        if (data.RightFootPosition.HasValue) Vector3Serializer.Serialize(writer, data.RightFootPosition.Value);
        if (data.RightFootRotation.HasValue) QuaternionSerializer.Serialize(writer, data.RightFootRotation.Value);
    }

    public static PlayerTrackingData Deserialize(NetDataReader reader)
    {
        DataFlags flags = (DataFlags)reader.GetUInt();

        return new PlayerTrackingData
        {
            Position = flags.HasFlag(DataFlags.Position) ? Vector3Serializer.Deserialize(reader) : null,
            MoveDirection = flags.HasFlag(DataFlags.MoveDirection) ? Vector2Serializer.Deserialize(reader) : null,
            RotationY = flags.HasFlag(DataFlags.RotationY) ? reader.GetFloat() : null,

            LookPosition = flags.HasFlag(DataFlags.LookPosition) ? reader.GetFloat() : null,

            SitHeight = flags.HasFlag(DataFlags.SitHeight) ? reader.GetFloat() : null,

            LeftHandPosition = flags.HasFlag(DataFlags.LeftHandPosition) ? Vector3Serializer.Deserialize(reader) : null,
            LeftHandRotation = flags.HasFlag(DataFlags.LeftHandRotation) ? QuaternionSerializer.Deserialize(reader) : null,
            // If flag is missing hand is open, if flag is present hand is closed
            LeftHandOpen = !flags.HasFlag(DataFlags.LeftHandClosed),

            RightHandPosition = flags.HasFlag(DataFlags.RightHandPosition) ? Vector3Serializer.Deserialize(reader) : null,
            RightHandRotation = flags.HasFlag(DataFlags.RightHandRotation) ? QuaternionSerializer.Deserialize(reader) : null,
            // If flag is missing hand is open, if flag is present hand is closed
            RightHandOpen = !flags.HasFlag(DataFlags.RightHandClosed
            ),

            HipPosition = flags.HasFlag(DataFlags.HipPosition) ? Vector3Serializer.Deserialize(reader) : null,
            HipRotation = flags.HasFlag(DataFlags.HipRotation) ? QuaternionSerializer.Deserialize(reader) : null,

            LeftFootPosition = flags.HasFlag(DataFlags.LeftFootPosition) ? Vector3Serializer.Deserialize(reader) : null,
            LeftFootRotation = flags.HasFlag(DataFlags.LeftFootRotation) ? QuaternionSerializer.Deserialize(reader) : null,

            RightFootPosition = flags.HasFlag(DataFlags.RightFootPosition) ? Vector3Serializer.Deserialize(reader) : null,
            RightFootRotation = flags.HasFlag(DataFlags.RightFootRotation) ? QuaternionSerializer.Deserialize(reader) : null
        };
    }

    /// <summary>Merges non-null fields from <paramref name="delta"/> into this instance.</summary>
    public PlayerTrackingData MergeFrom(PlayerTrackingData delta)
    {
        return new PlayerTrackingData
        {
            Position = delta.Position ?? Position,
            MoveDirection = delta.MoveDirection ?? MoveDirection,
            RotationY = delta.RotationY ?? RotationY,
            LookPosition = delta.LookPosition ?? LookPosition,
            SitHeight = delta.SitHeight ?? SitHeight,
            LeftHandPosition = delta.LeftHandPosition ?? LeftHandPosition,
            LeftHandRotation = delta.LeftHandRotation ?? LeftHandRotation,
            LeftHandOpen = delta.LeftHandOpen == null ? true : delta.LeftHandOpen,
            RightHandPosition = delta.RightHandPosition ?? RightHandPosition,
            RightHandRotation = delta.RightHandRotation ?? RightHandRotation,
            RightHandOpen = delta.RightHandOpen == null ? true : delta.RightHandOpen,
            HipPosition = delta.HipPosition ?? HipPosition,
            HipRotation = delta.HipRotation ?? HipRotation,
            LeftFootPosition = delta.LeftFootPosition ?? LeftFootPosition,
            LeftFootRotation = delta.LeftFootRotation ?? LeftFootRotation,
            RightFootPosition = delta.RightFootPosition ?? RightFootPosition,
            RightFootRotation = delta.RightFootRotation ?? RightFootRotation,
        };
    }
}
