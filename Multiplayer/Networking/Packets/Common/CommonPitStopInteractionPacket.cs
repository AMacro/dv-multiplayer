using DV.ThingTypes;

namespace Multiplayer.Networking.Packets.Common;

public class CommonPitStopInteractionPacket
{
    public ushort NetId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public float State { get; set; }
}
