namespace Multiplayer.Networking.Packets.Clientbound.Train;

public class ClientboundFireboxStatePacket
{
    public ushort NetId { get; set; }
    public float Contents { get; set; }
    public bool IsOn { get; set; }
}
