
namespace Multiplayer.Networking.Packets.Common;

public class CommonPitStopInteractionPacket
{
    public ushort NetId { get; set; }
    public byte InteractionType { get; set; }
    public int ResourceType { get; set; }
    public float State { get; set; }
}
