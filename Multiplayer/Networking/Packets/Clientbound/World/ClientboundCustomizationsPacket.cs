using Multiplayer.Networking.Data.Items;

namespace Multiplayer.Networking.Packets.Clientbound.World;

public class ClientboundCustomizationsPacket
{
    public CustomizationHoleData[] Holes { get; set; }
}
