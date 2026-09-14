using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class RemoveHolePacket : CustomizationActionPacket, INetSerializable
{
    public string TargetKey;
    public Vector3 PreviousPosition;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(TargetKey ?? string.Empty);
        Vector3Serializer.Serialize(writer, PreviousPosition);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); TargetKey = reader.GetString();
        PreviousPosition = Vector3Serializer.Deserialize(reader);
    }
}
