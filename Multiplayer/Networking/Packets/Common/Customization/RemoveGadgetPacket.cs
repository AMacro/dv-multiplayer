using LiteNetLib.Utils;

namespace Multiplayer.Networking.Packets.Common.Customization;

internal sealed class RemoveGadgetPacket : CustomizationActionPacket, INetSerializable
{
    public ushort ItemNetId;
    public bool ReparentToTrainCar;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(OriginActionId); writer.Put(ItemNetId); writer.Put(ReparentToTrainCar);
    }

    public void Deserialize(NetDataReader reader)
    {
        OriginActionId = reader.GetUInt(); ItemNetId = reader.GetUShort(); ReparentToTrainCar = reader.GetBool();
    }
}
