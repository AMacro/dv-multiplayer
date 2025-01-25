namespace Multiplayer.Networking.Packets.Serverbound.Train;

public class ServerboundAddCoalPacket
{
    public ushort NetId { get; set; }
    public float CoalMassDelta { get; set; }
}
