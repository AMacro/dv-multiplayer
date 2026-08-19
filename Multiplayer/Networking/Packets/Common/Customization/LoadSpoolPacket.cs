using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class LoadSpoolPacket : CustomizationActionPacket, INetSerializable
{
    public ushort ToolItemNetId;
    public ushort SpoolItemNetId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(ToolItemNetId); writer.Put(SpoolItemNetId);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); ToolItemNetId = reader.GetUShort(); SpoolItemNetId = reader.GetUShort();
    }
}
