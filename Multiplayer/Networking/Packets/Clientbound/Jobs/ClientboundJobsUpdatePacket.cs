using Multiplayer.Networking.Data;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.World;
using System.Collections.Generic;

namespace Multiplayer.Networking.Packets.Clientbound.Jobs;
public class ClientboundJobsUpdatePacket
{
    public ushort StationNetId { get; set; }
    public JobUpdateStruct[] JobUpdates { get; set; }

    
    public static ClientboundJobsUpdatePacket FromNetworkedJobs(ushort stationNetID, NetworkedJob[] jobs)
    {
        Multiplayer.Log($"ClientboundJobsUpdatePacket.FromNetworkedJobs({stationNetID}, {jobs.Length})");

        List<JobUpdateStruct> jobData = new List<JobUpdateStruct>();
        foreach (var job in jobs)
        {
            ushort validationStationNetId = 0;
            ushort validationItemNetId = 0;
            ItemPositionData itemPositionData = new ItemPositionData();

            if (NetworkedStationController.GetFromJobValidator(job.JobValidator, out NetworkedStationController netValidationStation))
                validationStationNetId = netValidationStation.NetId;

            switch (job.Cause)
            {
                case NetworkedJob.DirtyCause.JobOverview:
                    validationItemNetId = job.JobOverview.NetId;
                    itemPositionData = ItemPositionData.FromItem(job.JobOverview);
                    break;
                case NetworkedJob.DirtyCause.JobBooklet:
                    validationItemNetId = job.JobBooklet.NetId;
                    itemPositionData = ItemPositionData.FromItem(job.JobBooklet);
                    break;
                case NetworkedJob.DirtyCause.JobReport:
                    validationItemNetId = job.JobReport.NetId;
                    itemPositionData = ItemPositionData.FromItem(job.JobReport);
                    break;
            }

            JobUpdateStruct data = new JobUpdateStruct
            {
                JobNetID = job.NetId,
                JobState = job.Job.State,
                StartTime = job.Job.startTime,
                FinishTime = job.Job.finishTime,
                ValidationStationId = validationStationNetId,
                ItemNetID = validationItemNetId,
                ItemPositionData = itemPositionData
            };

            jobData.Add(data);
        }

        return new ClientboundJobsUpdatePacket
                                                {
                                                    StationNetId = stationNetID,
                                                    JobUpdates = jobData.ToArray()
                                                };
    }
}
