using LiteNetLib.Utils;
using Multiplayer.Networking.Data.Customization;

namespace Multiplayer.Networking.Packets.Clientbound;

public sealed class ClientboundCustomizationStatePacket : INetSerializable
{
    public CustomizationStateData State = new();

    public void Serialize(NetDataWriter writer)
    {
        State.Serialize(writer);
    }

    public void Deserialize(NetDataReader reader)
    {
        State = CustomizationStateData.Deserialize(reader);
    }
}
