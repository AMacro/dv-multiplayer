using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class UnsnapItemPacket : CustomizationActionPacket, INetSerializable
{
    public ushort GadgetItemNetId;
    public ushort SnappedItemNetId;
    public int SnapPointIndex;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(GadgetItemNetId); writer.Put(SnappedItemNetId); writer.Put(SnapPointIndex);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); GadgetItemNetId = reader.GetUShort();
        SnappedItemNetId = reader.GetUShort(); SnapPointIndex = reader.GetInt();
    }
}
