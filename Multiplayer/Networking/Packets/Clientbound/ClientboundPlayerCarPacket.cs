namespace Multiplayer.Networking.Packets.Clientbound;

public class ClientboundPlayerCarPacket
{
    public byte Id { get; set; }
    public ushort CarId { get; set; }
}
