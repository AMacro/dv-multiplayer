using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class ReplaceDuctTapePacket : CustomizationActionPacket, INetSerializable
{
    public ushort ConsumedItemNetId;
    public ushort ReplacementItemNetId;
    public byte OwnerPlayerId;
    public bool HasWorldTransform;
    public Vector3 Position;
    public Quaternion Rotation;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(ConsumedItemNetId); writer.Put(ReplacementItemNetId);
        writer.Put(OwnerPlayerId); writer.Put(HasWorldTransform);
        if (HasWorldTransform) { Vector3Serializer.Serialize(writer, Position); QuaternionSerializer.Serialize(writer, Rotation); }
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); ConsumedItemNetId = reader.GetUShort(); ReplacementItemNetId = reader.GetUShort();
        OwnerPlayerId = reader.GetByte(); HasWorldTransform = reader.GetBool();
        if (HasWorldTransform) { Position = Vector3Serializer.Deserialize(reader); Rotation = QuaternionSerializer.Deserialize(reader); }
    }
}
