using System.Collections.Generic;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
namespace Multiplayer.Networking.Packets.Clientbound.Jobs;

public class ClientboundJobsCreatePacket
{
    public ushort StationNetId { get; set; }
    public JobData[] Jobs { get; set; }

    public static ClientboundJobsCreatePacket FromNetworkedJobs(NetworkedStationController netStation, NetworkedJob[] jobs)
    {
        List<JobData> jobData = [];
        foreach (var job in jobs)
        {
            JobData jd = JobData.FromJob(netStation, job);
            Multiplayer.Log($"JobData: jobNetId: {jd.NetID}, jobId: {jd.ID}, itemNetId {jd.ItemNetID}");
            jobData.Add(jd);
        }

        return new ClientboundJobsCreatePacket
        {
            StationNetId = netStation.NetId,
            Jobs = jobData.ToArray()
        };
    }
}
