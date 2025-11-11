namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPingUpdatePacket
{
    public byte PlayerId { get; set; }
    public int Ping { get; set; }
}
