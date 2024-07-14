using Multiplayer.Networking.Data;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

public class ClientboundJobsPacket
{
    public string stationId { get; set; }
    public ushort[] netIds { get; set; }
    public JobData[] Jobs { get; set; }
    
}
