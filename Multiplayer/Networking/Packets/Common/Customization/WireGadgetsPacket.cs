using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class WireGadgetsPacket : CustomizationActionPacket, INetSerializable
{
    public ushort FirstItemNetId;
    public ushort SecondItemNetId;
    public int FirstPortIndex;
    public int SecondPortIndex;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(FirstItemNetId); writer.Put(SecondItemNetId);
        writer.Put(FirstPortIndex); writer.Put(SecondPortIndex);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); FirstItemNetId = reader.GetUShort(); SecondItemNetId = reader.GetUShort();
        FirstPortIndex = reader.GetInt(); SecondPortIndex = reader.GetInt();
    }
}
