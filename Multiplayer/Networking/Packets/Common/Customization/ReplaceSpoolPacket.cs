using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class ReplaceSpoolPacket : CustomizationActionPacket, INetSerializable
{
    public ushort ToolItemNetId;
    public ushort ReplacementSpoolItemNetId;
    public byte OwnerPlayerId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(ToolItemNetId); writer.Put(ReplacementSpoolItemNetId);
        writer.Put(OwnerPlayerId);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); ToolItemNetId = reader.GetUShort();
        ReplacementSpoolItemNetId = reader.GetUShort(); OwnerPlayerId = reader.GetByte();
    }
}
