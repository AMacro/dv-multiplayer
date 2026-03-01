using LiteNetLib.Utils;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Serialization;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Networking.Data.World;

public readonly struct PitStopPlugData(ushort netId, PlugInteractionType state, byte playerId, ushort trainCarNetId, sbyte socketIndex, Vector3 pos, Quaternion rot)
{
    public readonly ushort NetId = netId;
    public readonly byte PlayerId = playerId;
    public readonly PlugInteractionType State = state;
    public readonly ushort TrainCarNetId = trainCarNetId;
    public readonly sbyte SocketIndex = socketIndex;
    public readonly Vector3 Position = pos;
    public readonly Quaternion Rotation = rot;

    public static PitStopPlugData From(NetworkedPluggableObject plugData, bool bulk = false)
    {
        var interaction = GetInteractionType(plugData, bulk);

        Multiplayer.LogDebug(() => $"PitStopPlugData.From() NetId: {plugData.NetId}, Interaction: {interaction}");
        return new PitStopPlugData
                (
                    plugData.NetId,
                    interaction,
                    plugData.HeldBy?.PlayerId ?? 0,
                    plugData.TrainCarNetId,
                    plugData.SocketIndex,
                    plugData.transform.GetWorldAbsolutePosition(),
                    plugData.transform.rotation
                );
    }

    public static void Serialize(NetDataWriter writer, PitStopPlugData data)
    {
        writer.Put(data.NetId);
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
                writer.Put(data.SocketIndex);
                break;
        }
    }

    public static PitStopPlugData Deserialize(NetDataReader reader)
    {
        ushort netId = reader.GetUShort();
        PlugInteractionType state = (PlugInteractionType)reader.GetByte();
        byte playerId = 0;
        ushort trainCarNetId = 0;
        sbyte socketIndex = -1;
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
                socketIndex = reader.GetSByte();
                break;
        }

        return new PitStopPlugData
            (
                netId,
                state,
                playerId,
                trainCarNetId,
                socketIndex,
                pos,
                rot
            );
    }

    private static PlugInteractionType GetInteractionType(NetworkedPluggableObject netPlug, bool bulk)
    {
        Multiplayer.LogDebug(() => $"NetworkedPluggableObject.GetInteractionType() netId: {netPlug.NetId} bulk: {bulk}, Heldby:{netPlug.HeldBy}, TrainCarNetId: {netPlug.TrainCarNetId} socket not null: {netPlug.PluggableObject.Socket != null}, socket path: {netPlug.PluggableObject.Socket?.GetObjectPath()}, start attached to not null: {netPlug.PluggableObject.startAttachedTo != null}, start attached to path: {netPlug.PluggableObject.startAttachedTo?.GetObjectPath()}");
        //if (!bulk)
        //    return plugData.CurrentInteraction;

        if (netPlug.HeldBy != null)
            return PlugInteractionType.PickedUp;

        if (netPlug.PluggableObject.Socket == null)
            return PlugInteractionType.Dropped;

        if (netPlug.PluggableObject.Socket == netPlug.PluggableObject.startAttachedTo)
            return PlugInteractionType.DockHome;

        if (netPlug.TrainCarNetId != 0)
            return PlugInteractionType.DockSocket;

        return PlugInteractionType.Rejected;
    }
}
