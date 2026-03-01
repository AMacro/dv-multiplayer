
using LiteNetLib.Utils;
using Multiplayer.Networking.Data.World;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common;

public class CommonPitStopPlugInteractionPacket : INetSerializable
{
    public ushort NetId { get; set; }
    public PlugInteractionType InteractionType { get; set; }
    public byte PlayerId { get; set; }
    public ushort TrainCarNetId { get; set; }
    public sbyte SocketIndex { get; set; }
    public Vector3? Position { get; set; }
    public Quaternion? Rotation { get; set; }
    public Vector3? YankForce { get; set; }
    public ForceMode YankMode { get; set; }

    public void Deserialize(NetDataReader reader)
    {
        NetId = reader.GetUShort();
        InteractionType = (PlugInteractionType)reader.GetByte();

        switch (InteractionType)
        {
            case PlugInteractionType.Rejected:
                break;

            case PlugInteractionType.PickedUp:
                PlayerId = reader.GetByte();
                break;

            case PlugInteractionType.Dropped:
                Position = Vector3Serializer.Deserialize(reader);
                Rotation = QuaternionSerializer.Deserialize(reader);
                break;

            case PlugInteractionType.Yanked:
                Position = Vector3Serializer.Deserialize(reader);
                Rotation = QuaternionSerializer.Deserialize(reader);

                YankForce = Vector3Serializer.Deserialize(reader);
                YankMode = (ForceMode)reader.GetByte();
                break;

            case PlugInteractionType.DockHome:
                break;

            case PlugInteractionType.DockSocket:
                TrainCarNetId = reader.GetUShort();
                SocketIndex = reader.GetSByte();
                break;
        }
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(NetId);
        writer.Put((byte)InteractionType);

        switch (InteractionType)
        {
            case PlugInteractionType.Rejected:
                break;

            case PlugInteractionType.PickedUp:
                writer.Put(PlayerId);
                break;

            case PlugInteractionType.Dropped:
                Vector3Serializer.Serialize(writer, Position ?? Vector3.zero);
                QuaternionSerializer.Serialize(writer, Rotation ?? Quaternion.identity);
                break;

            case PlugInteractionType.Yanked:
                Vector3Serializer.Serialize(writer, Position ?? Vector3.zero);
                QuaternionSerializer.Serialize(writer, Rotation ?? Quaternion.identity);

                Vector3Serializer.Serialize(writer, YankForce ?? Vector3.zero);
                writer.Put((byte)YankMode);
                break;

            case PlugInteractionType.DockHome:
                break;

            case PlugInteractionType.DockSocket:
                writer.Put(TrainCarNetId);
                writer.Put(SocketIndex);
                break;
        }
    }
}
