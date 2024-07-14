using System.Linq;
using DV.Logic.Job;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace Multiplayer.Networking.Data;

public class JobData
{
    public byte JobType { get; set; }
    public string ID { get; set; }
    public TaskBeforeDataData[] Tasks { get; set; }
    public StationsChainDataData ChainData { get; set; }
    public int RequiredLicenses { get; set; }
    public float StartTime { get; set; }
    public float FinishTime { get; set; }
    public float InitialWage { get; set; }
    public byte State { get; set; }
    public float TimeLimit { get; set; }

    public static JobData FromJob(Job job)
    {
        return new JobData
        {
            JobType = (byte)job.jobType,
            ID = job.ID,
            Tasks = job.tasks.Select(x => TaskBeforeDataData.FromTask(x)).ToArray(),
            ChainData = StationsChainDataData.FromStationData(job.chainData),
            RequiredLicenses = (int)job.requiredLicenses,
            StartTime = job.startTime,
            FinishTime = job.finishTime,
            InitialWage = job.initialWage,
            State = (byte)job.State,
            TimeLimit = job.TimeLimit
        };
    }

    public static void Serialize(NetDataWriter writer, JobData data)
    {
        writer.Put(data.JobType);
        writer.Put(data.ID);
        writer.Put((byte)data.Tasks.Length);
        foreach (var taskBeforeDataData in data.Tasks)
            TaskBeforeDataData.SerializeTask(taskBeforeDataData, writer);
        StationsChainDataData.Serialize(writer, data.ChainData);
        writer.Put(data.RequiredLicenses);
        writer.Put(data.StartTime);
        writer.Put(data.FinishTime);
        writer.Put(data.InitialWage);
        writer.Put(data.State);
        writer.Put(data.TimeLimit);
        Multiplayer.Log(JsonConvert.SerializeObject(data, Formatting.None));
    }

    public static JobData Deserialize(NetDataReader reader)
    {
        Multiplayer.Log("JobData.Deserialize()");
        var jobType = reader.GetByte();
        Multiplayer.Log("JobData.Deserialize() jobType: " + jobType);
        var id = reader.GetString();
        Multiplayer.Log("JobData.Deserialize() id: " + id);
        var tasksLength = reader.GetByte();
        Multiplayer.Log("JobData.Deserialize() tasksLength: " + tasksLength);
        var tasks = new TaskBeforeDataData[tasksLength];
        for (int i = 0; i < tasksLength; i++)
            tasks[i] = TaskBeforeDataData.DeserializeTask(reader);
        //Multiplayer.Log("JobData.Deserialize() tasks: " + JsonConvert.SerializeObject(tasks, Formatting.None));
        var chainData = StationsChainDataData.Deserialize(reader);
        //Multiplayer.Log("JobData.Deserialize() chainData: " + JsonConvert.SerializeObject(chainData, Formatting.Indented));
        var requiredLicenses = reader.GetInt();
        Multiplayer.Log("JobData.Deserialize() requiredLicenses: " + requiredLicenses);
        var startTime = reader.GetFloat();
        Multiplayer.Log("JobData.Deserialize() startTime: " + startTime);
        var finishTime = reader.GetFloat();
        Multiplayer.Log("JobData.Deserialize() finishTime: " + finishTime);
        var initialWage = reader.GetFloat();
        Multiplayer.Log("JobData.Deserialize() initialWage: " + initialWage);
        var state = reader.GetByte();
        Multiplayer.Log("JobData.Deserialize() state: " + state);
        var timeLimit = reader.GetFloat();
        Multiplayer.Log(JsonConvert.SerializeObject(new JobData
        {
            JobType = jobType,
            ID = id,
            Tasks = tasks,
            ChainData = chainData,
            RequiredLicenses = requiredLicenses,
            StartTime = startTime,
            FinishTime = finishTime,
            InitialWage = initialWage,
            State = state,
            TimeLimit = timeLimit
        }, Formatting.None));

        return new JobData
        {
            JobType = jobType,
            ID = id,
            Tasks = tasks,
            ChainData = chainData,
            RequiredLicenses = requiredLicenses,
            StartTime = startTime,
            FinishTime = finishTime,
            InitialWage = initialWage,
            State = state,
            TimeLimit = timeLimit
        };
    }
}

public struct StationsChainDataData
{
    public string ChainOriginYardId { get; set; }
    public string ChainDestinationYardId { get; set; }

    public static StationsChainDataData FromStationData(StationsChainData data)
    {
        return new StationsChainDataData
        {
            ChainOriginYardId = data.chainOriginYardId,
            ChainDestinationYardId = data.chainDestinationYardId
        };
    }

    public static void Serialize(NetDataWriter writer, StationsChainDataData data)
    {
        writer.Put(data.ChainOriginYardId);
        writer.Put(data.ChainDestinationYardId);
    }

    public static StationsChainDataData Deserialize(NetDataReader reader)
    {
        return new StationsChainDataData
        {
            ChainOriginYardId = reader.GetString(),
            ChainDestinationYardId = reader.GetString()
        };
    }
}
