using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class LoadSpoolPacket : CustomizationActionPacket, INetSerializable
{
    public ushort ToolItemNetId;
    public ushort SpoolItemNetId;
    public bool HasRemainingUnits;
    public int RemainingUnits;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(ToolItemNetId); writer.Put(SpoolItemNetId);
        writer.Put(HasRemainingUnits);
        if (HasRemainingUnits) writer.Put(RemainingUnits);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); ToolItemNetId = reader.GetUShort(); SpoolItemNetId = reader.GetUShort();
        HasRemainingUnits = reader.GetBool();
        RemainingUnits = HasRemainingUnits ? reader.GetInt() : 0;
    }
}
