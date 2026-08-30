using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class DropEmptySpoolPacket : CustomizationActionPacket, INetSerializable
{
    public ushort ToolItemNetId;
    public ushort SpoolItemNetId;
    public bool HasWorldTransform;
    public Vector3 Position;
    public Quaternion Rotation;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(ToolItemNetId); writer.Put(SpoolItemNetId); writer.Put(HasWorldTransform);
        if (HasWorldTransform) { Vector3Serializer.Serialize(writer, Position); QuaternionSerializer.Serialize(writer, Rotation); }
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); ToolItemNetId = reader.GetUShort(); SpoolItemNetId = reader.GetUShort();
        HasWorldTransform = reader.GetBool();
        if (HasWorldTransform) { Position = Vector3Serializer.Deserialize(reader); Rotation = QuaternionSerializer.Deserialize(reader); }
    }
}
