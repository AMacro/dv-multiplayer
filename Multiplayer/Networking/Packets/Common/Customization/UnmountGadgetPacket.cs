using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class UnmountGadgetPacket : CustomizationActionPacket, INetSerializable
{
    public ushort MountItemNetId;
    public int MountIndex;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(MountItemNetId); writer.Put(MountIndex);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); MountItemNetId = reader.GetUShort(); MountIndex = reader.GetInt();
    }
}
