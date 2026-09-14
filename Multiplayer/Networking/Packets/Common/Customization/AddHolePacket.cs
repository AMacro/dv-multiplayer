using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class AddHolePacket : CustomizationActionPacket, INetSerializable
{
    public string TargetKey;
    public Vector3 Position;
    public Vector3 Normal;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(TargetKey ?? string.Empty);
        Vector3Serializer.Serialize(writer, Position); Vector3Serializer.Serialize(writer, Normal);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); TargetKey = reader.GetString();
        Position = Vector3Serializer.Deserialize(reader); Normal = Vector3Serializer.Deserialize(reader);
    }
}
