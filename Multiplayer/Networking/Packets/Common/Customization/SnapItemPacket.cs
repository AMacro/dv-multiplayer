using LiteNetLib.Utils;
using Multiplayer.Networking.Serialization;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class SnapItemPacket : CustomizationActionPacket, INetSerializable
{
    public ushort GadgetItemNetId;
    public ushort SnappedItemNetId;
    public int SnapPointIndex;
    public bool HasAnchorPosition;
    public Vector3 AnchorPosition;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(GadgetItemNetId); writer.Put(SnappedItemNetId);
        writer.Put(SnapPointIndex); writer.Put(HasAnchorPosition);
        if (HasAnchorPosition) Vector3Serializer.Serialize(writer, AnchorPosition);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); GadgetItemNetId = reader.GetUShort(); SnappedItemNetId = reader.GetUShort();
        SnapPointIndex = reader.GetInt(); HasAnchorPosition = reader.GetBool();
        if (HasAnchorPosition) AnchorPosition = Vector3Serializer.Deserialize(reader);
    }
}
