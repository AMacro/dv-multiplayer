using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Data;

public readonly struct PitStopPlugData(ushort netId, PlugInteractionType state, byte playerId, ushort trainCarNetId, bool isLeft, Vector3 pos, Quaternion rot)
{
    public readonly ushort NetID = netId;
    public readonly byte PlayerId = playerId;
    public readonly PlugInteractionType State = state;
    public readonly ushort TrainCarNetId = trainCarNetId;
    public readonly bool IsLeftSide = isLeft;
    public readonly Vector3 Position = pos;
    public readonly Quaternion Rotation = rot;

    public static PitStopPlugData From(NetworkedPluggableObject plugData)
    {
        return new PitStopPlugData
            (
                plugData.NetId,
                plugData.CurrentInteration,
                plugData.HeldBy.Id,
                plugData.TrainCarNetId,
                plugData.IsConnectedLeft,
                plugData.transform.AbsolutePosition(),
                plugData.transform.rotation
            );
    }

    public static void Serialize(NetDataWriter writer, PitStopPlugData data)
    {
        writer.Put(data.NetID);
        writer.Put((byte)data.State);

        switch (data.State)
        {
            case PlugInteractionType.Rejected:
                //do nothing??
                break;
            case PlugInteractionType.PickedUp:
                writer.Put(data.PlayerId);
                break;
            case PlugInteractionType.Dropped:
                Vector3Serializer.Serialize(writer, data.Position);
                QuaternionSerializer.Serialize(writer, data.Rotation);
                break;
            case PlugInteractionType.DockHome:
                //do nothing
                break;
            case PlugInteractionType.DockSocket:
                writer.Put(data.TrainCarNetId);
                writer.Put(data.IsLeftSide);
                break;
        }
    }

    public static PitStopPlugData Deserialize(NetDataReader reader)
    {
        ushort netId = reader.GetUShort();
        PlugInteractionType state = (PlugInteractionType)reader.GetByte();
        byte playerId = 0;
        ushort trainCarNetId = 0;
        bool isLeft = false;
        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;

        switch (state)
        {
            case PlugInteractionType.Rejected:
                // No additional data to read
                break;
            case PlugInteractionType.PickedUp:
                playerId = reader.GetByte();
                break;
            case PlugInteractionType.Dropped:
                pos = Vector3Serializer.Deserialize(reader);
                rot = QuaternionSerializer.Deserialize(reader);
                break;
            case PlugInteractionType.DockHome:
                // No additional data to read
                break;
            case PlugInteractionType.DockSocket:
                trainCarNetId = reader.GetUShort();
                isLeft = reader.GetBool();
                break;
        }

        return new PitStopPlugData
            (
                netId,
                state,
                playerId,
                trainCarNetId,
                isLeft,
                pos,
                rot
            );
    }
}
