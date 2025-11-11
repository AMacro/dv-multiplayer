using DV.Logic.Job;
using DV.ThingTypes;
using LiteNetLib.Utils;
using MPAPI.Types;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.World;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Multiplayer.Networking.Data;

public class JobData
{
    public ushort NetID { get; set; }
    public JobType JobType { get; set; } //serialise as byte
    public string ID { get; set; }
    public TaskNetworkData[] Tasks { get; set; }
    public StationsChainNetworkData ChainData { get; set; }
    public JobLicenses RequiredLicenses { get; set; } //serialise as int
    public float StartTime { get; set; }
    public float FinishTime { get; set; }
    public float InitialWage { get; set; }
    public JobState State { get; set; } //serialise as byte
    public float TimeLimit { get; set; }
    public ushort ItemNetID { get; set; }
    public ItemPositionData ItemPosition { get; set; }

    public static JobData FromJob(NetworkedStationController netStation, NetworkedJob networkedJob)
    {
        Job job = networkedJob.Job;

        ushort itemNetId = 0;
        ItemPositionData itemPos = new();

        //Multiplayer.Log($"JobData.FromJob({netStation.name}, {job.ID}, {networkedJob.Job.Value})");

        if (networkedJob.Job.State == JobState.Available)
        {
            if (networkedJob.JobOverview != null)
            {
                itemNetId = networkedJob.JobOverview.NetId;
                itemPos = ItemPositionData.FromItem(networkedJob.JobOverview);
            }
        }
        else if (job.State == JobState.InProgress)
        {
            if (networkedJob.JobBooklet != null)
            {
                itemNetId = networkedJob.JobBooklet.NetId;
                itemPos = ItemPositionData.FromItem(networkedJob.JobBooklet);
            }
        }
        else if (job.State == JobState.Completed)
        {
            if (networkedJob.JobReport != null)
            {
                itemNetId = networkedJob.JobReport.NetId;
                itemPos = ItemPositionData.FromItem(networkedJob.JobReport);
            }
        }

        return new JobData
        {
            NetID = networkedJob.NetId,
            JobType = job.jobType,
            ID = job.ID,
            Tasks = TaskNetworkDataFactory.ConvertTasks(job.tasks),
            ChainData = StationsChainNetworkData.FromStationData(job.chainData),
            RequiredLicenses = job.requiredLicenses,
            StartTime = job.startTime,
            FinishTime = job.finishTime,
            InitialWage = job.initialWage,
            State = job.State,
            TimeLimit = job.TimeLimit,
            ItemNetID = itemNetId,
            ItemPosition = itemPos,
        };
    }

    public static (Job newJob, Dictionary<ushort, Task> netIdToTask) ToJob(JobData jobData)
    {
        Dictionary<ushort, Task> netIdToTask = [];

        List<Task> tasks = jobData.Tasks.Select(taskData => taskData.ToTask(ref netIdToTask)).ToList();

        StationsChainData chainData = new(jobData.ChainData.ChainOriginYardId, jobData.ChainData.ChainDestinationYardId);

        Job newJob = new(tasks, jobData.JobType, jobData.TimeLimit, jobData.InitialWage, chainData, jobData.ID, jobData.RequiredLicenses)
        {
            startTime = jobData.StartTime,
            finishTime = jobData.FinishTime,
            State = jobData.State,
        };

        return new(newJob, netIdToTask);
    }

    public static void Serialize(NetDataWriter writer, JobData data)
    {
        //NetworkLifecycle.Instance.Server.Log($"JobData.Serialize({data.ID}) NetID {data.NetID}");

        writer.Put(data.NetID);
        writer.Put((byte)data.JobType);
        writer.Put(data.ID);

        //task data - add compression
        using (MemoryStream ms = new())
        using (BinaryWriter bw = new(ms))
        {
            bw.Write((byte)data.Tasks.Length);
            foreach (var task in data.Tasks)
            {
                bw.Write((byte)task.TaskType);

                using (MemoryStream taskMemStream = new())
                using (BinaryWriter taskSerialiser = new(taskMemStream))
                {
                    task.Serialize(taskSerialiser);

                    if (taskMemStream.Length > int.MaxValue)
                    {
                        Multiplayer.LogError($"Task {task.TaskType} too large: {taskMemStream.Length}");
                        throw new InvalidOperationException($"Task {task.TaskType} data is too large to serialize.");
                    }

                    bw.Write((int)taskMemStream.Length);
                    bw.Write(taskMemStream.ToArray());
                }
            }

            byte[] compressedData = PacketCompression.Compress(ms.ToArray());

            // Multiplayer.Log($"JobData.Serialize() Uncompressed: {ms.Length} Compressed: {compressedData.Length}");
            writer.PutBytesWithLength(compressedData);
        }

        StationsChainNetworkData.Serialize(writer, data.ChainData);

        writer.Put((int)data.RequiredLicenses);
        writer.Put(data.StartTime);
        writer.Put(data.FinishTime);
        writer.Put(data.InitialWage);
        writer.Put((byte)data.State);
        writer.Put(data.TimeLimit);
        writer.Put(data.ItemNetID);
        ItemPositionData.Serialize(writer, data.ItemPosition);
    }

    public static JobData Deserialize(NetDataReader reader)
    {
        ushort netID = 0;
        JobType jobType = JobType.Custom;
        string id = string.Empty;

        try
        {
            netID = reader.GetUShort();
            jobType = (JobType)reader.GetByte();
            id = reader.GetString();

            //Decompress task data
            byte[] compressedData = reader.GetBytesWithLength();
            byte[] decompressedData = PacketCompression.Decompress(compressedData);

            //Multiplayer.Log($"JobData.Deserialize() Compressed: {compressedData.Length} Decompressed: {decompressedData.Length}");

            TaskNetworkData[] tasks;

            using (MemoryStream ms = new MemoryStream(decompressedData))
            using (BinaryReader br = new BinaryReader(ms))
            {
                byte tasksLength = br.ReadByte();
                tasks = new TaskNetworkData[tasksLength];

                for (int i = 0; i < tasksLength; i++)
                {
                    TaskType taskType = (TaskType)br.ReadByte();

                    int taskLength = br.ReadInt32();

                    using (MemoryStream taskStream = new MemoryStream(br.ReadBytes(taskLength)))
                    using (BinaryReader taskReader = new BinaryReader(taskStream))
                    {
                        tasks[i] = TaskNetworkDataFactory.ConvertTask(taskType);
                        tasks[i].Deserialize(taskReader);
                    }
                }
            }

            StationsChainNetworkData chainData = StationsChainNetworkData.Deserialize(reader);

            JobLicenses requiredLicenses = (JobLicenses)reader.GetInt();
            float startTime = reader.GetFloat();
            float finishTime = reader.GetFloat();
            float initialWage = reader.GetFloat();
            JobState state = (JobState)reader.GetByte();
            float timeLimit = reader.GetFloat();
            ushort itemNetId = reader.GetUShort();
            ItemPositionData itemPositionData = ItemPositionData.Deserialize(reader);

            return new JobData
            {
                NetID = netID,
                JobType = jobType,
                ID = id,
                Tasks = tasks,
                ChainData = chainData,
                RequiredLicenses = requiredLicenses,
                StartTime = startTime,
                FinishTime = finishTime,
                InitialWage = initialWage,
                State = state,
                TimeLimit = timeLimit,
                ItemNetID = itemNetId,
                ItemPosition = itemPositionData
            };
        }
        catch (Exception ex)
        {
            Multiplayer.Log($"JobData.Deserialize() Failed! netId: {netID}, jobId: {id} {ex.Message}\r\n{ex.StackTrace}");
            return null;
        }
    }

    public List<ushort> GetCars()
    {
        List<ushort> result = [];

        foreach (var task in Tasks)
        {
            var cars = task.GetCars();
            result.AddRange(cars);
        }

        return result;
    }

}

public struct StationsChainNetworkData
{
    public string ChainOriginYardId { get; set; }
    public string ChainDestinationYardId { get; set; }

    public static StationsChainNetworkData FromStationData(StationsChainData data)
    {
        return new StationsChainNetworkData
        {
            ChainOriginYardId = data.chainOriginYardId,
            ChainDestinationYardId = data.chainDestinationYardId
        };
    }

    public static void Serialize(NetDataWriter writer, StationsChainNetworkData data)
    {
        writer.Put(data.ChainOriginYardId);
        writer.Put(data.ChainDestinationYardId);
    }

    public static StationsChainNetworkData Deserialize(NetDataReader reader)
    {
        return new StationsChainNetworkData
        {
            ChainOriginYardId = reader.GetString(),
            ChainDestinationYardId = reader.GetString()
        };
    }
}
