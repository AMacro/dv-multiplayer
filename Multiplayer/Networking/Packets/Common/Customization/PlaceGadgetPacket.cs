using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class PlaceGadgetPacket : CustomizationActionPacket, INetSerializable
{
    public ushort ItemNetId;
    public string TargetKey;
    public Vector3 Position;
    public Quaternion Rotation;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(ItemNetId); writer.Put(TargetKey ?? string.Empty);
        Vector3Serializer.Serialize(writer, Position); QuaternionSerializer.Serialize(writer, Rotation);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); ItemNetId = reader.GetUShort(); TargetKey = reader.GetString();
        Position = Vector3Serializer.Deserialize(reader); Rotation = QuaternionSerializer.Deserialize(reader);
    }
}
