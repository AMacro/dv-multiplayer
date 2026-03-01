using Multiplayer.Networking.Data.Jobs;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

public class ServerboundJobValidateRequestPacket
{
    public ushort JobNetId { get; set; }
    public ushort StationNetId { get; set; }
    public ValidationType validationType { get; set; }
}
