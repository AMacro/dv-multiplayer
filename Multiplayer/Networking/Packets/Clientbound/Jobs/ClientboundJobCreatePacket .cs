using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Networking.Data;
using Multiplayer.Networking.Packets.Clientbound.Train;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

public class ClientboundJobCreatePacket
{
    public ushort netId { get; set; }
    public string stationId { get; set; }
    public JobData job { get; set; }

    public static ClientboundJobCreatePacket FromNetworkedJob(NetworkedJob job)
    {
        return new ClientboundJobCreatePacket
        {
            netId = job.NetId,
            stationId = job.stationID,
            job = JobData.FromJob(job.job),
        };
    }
}
