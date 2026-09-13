using Multiplayer.Networking.Data.Jobs;

namespace Multiplayer.Networking.Packets.Serverbound.Jobs;

public class ServerboundJobsRequestPacket
{
    public uint StationNetId { get; set; }
    public bool GenerateJobs { get; set; }
    public ushort[] SpecificJobsNetIds { get; set; }
    public ushort[] ExcludeJobNetId { get; set; }
}
