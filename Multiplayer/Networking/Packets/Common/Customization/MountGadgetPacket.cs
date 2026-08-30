using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class MountGadgetPacket : CustomizationActionPacket, INetSerializable
{
    public ushort MountItemNetId;
    public ushort MountedItemNetId;
    public int MountIndex;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(MountItemNetId); writer.Put(MountedItemNetId); writer.Put(MountIndex);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); MountItemNetId = reader.GetUShort();
        MountedItemNetId = reader.GetUShort(); MountIndex = reader.GetInt();
    }
}
