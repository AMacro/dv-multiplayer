using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;

namespace Multiplayer.Networking.Data;

public abstract class TaskBeforeDataData
{
    public byte State { get; set; }
    public float TaskStartTime { get; set; }
    public float TaskFinishTime { get; set; }
    public bool IsLastTask { get; set; }
    public float TimeLimit { get; set; }
    public byte TaskType { get; set; }


    public static TaskBeforeDataData FromTask(Task task)
    {
        TaskBeforeDataData taskData = task switch
        {
            WarehouseTask warehouseTask => WarehouseTaskData.FromWarehouseTask(warehouseTask),
            TransportTask transportTask => TransportTaskData.FromTransportTask(transportTask),
            SequentialTasks sequentialTasks => SequentialTasksData.FromSequentialTask(sequentialTasks),
            ParallelTasks parallelTasks => ParallelTasksData.FromParallelTask(parallelTasks),
            _ => throw new ArgumentException("Unknown task type: " + task.GetType())
        };

        taskData.State = (byte)task.state;
        taskData.TaskStartTime = task.taskStartTime;
        taskData.TaskFinishTime = task.taskFinishTime;
        taskData.IsLastTask = task.IsLastTask;
        taskData.TimeLimit = task.TimeLimit;
        taskData.TaskType = (byte)task.InstanceTaskType;

        return taskData;
    }

    public static Task ToTask(object data)
    {
        if (data is WarehouseTaskData)
        {
            var task = (WarehouseTaskData)data;
            return WarehouseTaskData.ToWarehouseTask(task);
        }

        if (data is TransportTaskData)
        {
            var task = (TransportTaskData)data;
            return TransportTaskData.ToTransportTask(task);
        }

        if (data is SequentialTasksData)
        {
            var task = (SequentialTasksData)data;
            List<Task> tasks = new List<Task>();

            foreach (TaskBeforeDataData taskBeforeDataData in task.Tasks)
                tasks.Add(ToTask(taskBeforeDataData));


            return new SequentialTasks(tasks);
        }

        if (data is ParallelTasksData)
        {
            var task = (ParallelTasksData)data;
            List<Task> tasks = new List<Task>();

            foreach (TaskBeforeDataData taskBeforeDataData in task.Tasks)
                tasks.Add(ToTask(taskBeforeDataData));


            return new ParallelTasks(tasks);
        }

        throw new ArgumentException("Unknown task type: " + data.GetType());
    }

    public static void SerializeTask(object data, NetDataWriter writer)
    {
        if (data is WarehouseTaskData)
        {
            var task = (WarehouseTaskData)data;
            WarehouseTaskData.Serialize(writer, task);
            return;
        }

        if (data is TransportTaskData)
        {
            var task = (TransportTaskData)data;
            TransportTaskData.Serialize(writer, task);
            return;
        }

        if (data is SequentialTasksData)
        {
            var task = (SequentialTasksData)data;

            SequentialTasksData.Serialize(writer, task);

            return;
        }

        if (data is ParallelTasksData)
        {
            var task = (ParallelTasksData)data;

            ParallelTasksData.Serialize(writer, task);


            return;
        }

        throw new ArgumentException("Unknown task type: " + data.GetType());
    }

    public static TaskBeforeDataData DeserializeTask(NetDataReader reader)
    {
        TaskType taskType = (TaskType)reader.GetByte();
        Multiplayer.Log("Task type: " + taskType + "");

        return taskType switch
        {
            DV.Logic.Job.TaskType.Warehouse => WarehouseTaskData.Deserialize(reader),
            DV.Logic.Job.TaskType.Transport => TransportTaskData.Deserialize(reader),
            DV.Logic.Job.TaskType.Sequential => SequentialTasksData.Deserialize(reader),
            DV.Logic.Job.TaskType.Parallel => ParallelTasksData.Deserialize(reader),
            _ => throw new ArgumentException("Unknown task type: " + taskType)
        };
    }

    public static void Serialize(NetDataWriter writer, TaskBeforeDataData data)
    {
        writer.Put(data.TaskType);
        writer.Put(data.State);
        writer.Put(data.TaskStartTime);
        writer.Put(data.TaskFinishTime);
        writer.Put(data.IsLastTask);
        writer.Put(data.TimeLimit);
        writer.Put(data.TaskType);
    }

    public static void Deserialize(NetDataReader reader, TaskBeforeDataData data)
    {
        data.State = reader.GetByte();
        data.TaskStartTime = reader.GetFloat();
        data.TaskFinishTime = reader.GetFloat();
        data.IsLastTask = reader.GetBool();
        data.TimeLimit = reader.GetFloat();
        data.TaskType = reader.GetByte();
    }
}

public class ParallelTasksData : TaskBeforeDataData
{
    public TaskBeforeDataData[] Tasks { get; set; }

    public static ParallelTasksData FromParallelTask(ParallelTasks task)
    {
        return new ParallelTasksData
        {
            Tasks = task.tasks.Select(x => FromTask(x)).ToArray()
        };
    }

    public static void Serialize(NetDataWriter writer, ParallelTasksData data)
    {
        TaskBeforeDataData.Serialize(writer, data);
        writer.Put((byte)data.Tasks.Length);
        foreach (var taskBeforeDataData in data.Tasks)
            SerializeTask(taskBeforeDataData, writer);
    }

    public static ParallelTasksData Deserialize(NetDataReader reader)
    {
        var parallelTask = new ParallelTasksData();
        Deserialize(reader, parallelTask);
        var tasksLength = reader.GetByte();
        var tasks = new TaskBeforeDataData[tasksLength];
        for (int i = 0; i < tasksLength; i++)
            tasks[i] = DeserializeTask(reader);
        parallelTask.Tasks = tasks;
        return parallelTask;
    }
}

public class SequentialTasksData : TaskBeforeDataData
{
    public TaskBeforeDataData[] Tasks { get; set; }


    public static SequentialTasksData FromSequentialTask(SequentialTasks task)
    {
        return new SequentialTasksData
        {
            Tasks = task.tasks.Select(x => FromTask(x)).ToArray(),
        };
    }

    public static void Serialize(NetDataWriter writer, SequentialTasksData data)
    {
        TaskBeforeDataData.Serialize(writer, data);
        writer.Put((byte)data.Tasks.Length);
        foreach (var taskBeforeDataData in data.Tasks)
            SerializeTask(taskBeforeDataData, writer);
    }

    public static SequentialTasksData Deserialize(NetDataReader reader)
    {
        var sequentialTask = new SequentialTasksData();
        Deserialize(reader, sequentialTask);
        var tasksLength = reader.GetByte();
        var tasks = new TaskBeforeDataData[tasksLength];
        for (int i = 0; i < tasksLength; i++)
            tasks[i] = DeserializeTask(reader);
        sequentialTask.Tasks = tasks;
        return sequentialTask;
    }
}

public class WarehouseTaskData : TaskBeforeDataData
{
    public string[] Cars { get; set; }
    public byte WarehouseTaskType { get; set; }
    public string WarehouseMachine { get; set; }
    public CargoType CargoType { get; set; }
    public float CargoAmount { get; set; }
    public bool ReadyForMachine { get; set; }

    public static WarehouseTaskData FromWarehouseTask(WarehouseTask task)
    {
        return new WarehouseTaskData
        {
            Cars = task.cars.Select(x => x.ID).ToArray(),
            WarehouseTaskType = (byte)task.warehouseTaskType,
            WarehouseMachine = task.warehouseMachine.ID,
            CargoType = task.cargoType,
            CargoAmount = task.cargoAmount,
            ReadyForMachine = task.readyForMachine
        };
    }

    public static WarehouseTask ToWarehouseTask(WarehouseTaskData data)
    {
        return new WarehouseTask(
            CarSpawner.Instance.allCars.FindAll(x => data.Cars.Contains(x.ID)).Select(x => x.logicCar).ToList(),
            (WarehouseTaskType)data.WarehouseTaskType,
            JobSaveManager.Instance.GetWarehouseMachineWithId(data.WarehouseMachine),
            (CargoType)data.CargoType,
            data.CargoAmount
        );
    }

    public static void Serialize(NetDataWriter writer, WarehouseTaskData data)
    {
        TaskBeforeDataData.Serialize(writer, data);
        writer.PutArray(data.Cars);
        writer.Put(data.WarehouseTaskType);
        writer.Put(data.WarehouseMachine);
        writer.Put((int)data.CargoType);
        writer.Put(data.CargoAmount);
        writer.Put(data.ReadyForMachine);
    }

    public static WarehouseTaskData Deserialize(NetDataReader reader)
    {
        WarehouseTaskData data = new WarehouseTaskData();
        Deserialize(reader, data);
        data.Cars = reader.GetStringArray();
        data.WarehouseTaskType = reader.GetByte();
        data.WarehouseMachine = reader.GetString();
        data.CargoType = (CargoType)reader.GetInt();
        data.CargoAmount = reader.GetFloat();
        data.ReadyForMachine = reader.GetBool();

        return data;
    }
}

public class TransportTaskData : TaskBeforeDataData
{
    public string[] Cars { get; set; }
    public string StartingTrack { get; set; }
    public string DestinationTrack { get; set; }
    public CargoType[] TransportedCargoPerCar { get; set; }
    public bool CouplingRequiredAndNotDone { get; set; }
    public bool AnyHandbrakeRequiredAndNotDone { get; set; }

    public static TransportTaskData FromTransportTask(TransportTask task)
    {
        Multiplayer.Log("Cars: " + task.cars.Select(x => x.ID).ToArray().Join());
        Multiplayer.Log("FromTransportTask.TransportedCargoPerCar: " + task.transportedCargoPerCar?.Select(x => (int)x).ToArray().Join() + "\r\n\t"+ task.transportedCargoPerCar?.ToArray().Join());

        return new TransportTaskData
        {
            Cars = task.cars.Select(x => x.ID).ToArray(),
            StartingTrack = task.startingTrack.ID.RailTrackGameObjectID,
            DestinationTrack = task.destinationTrack.ID.RailTrackGameObjectID,
            TransportedCargoPerCar = task.transportedCargoPerCar?.ToArray(),
            CouplingRequiredAndNotDone = task.couplingRequiredAndNotDone,
            AnyHandbrakeRequiredAndNotDone = task.anyHandbrakeRequiredAndNotDone
        };
    }

    public static TransportTask ToTransportTask(TransportTaskData data)
    {
        return new TransportTask(
            CarSpawner.Instance.allCars.FindAll(x => data.Cars.Contains(x.ID)).Select(x => x.logicCar).ToList(),
            RailTrackRegistry.Instance.GetTrackWithName(data.DestinationTrack).logicTrack,
            RailTrackRegistry.Instance.GetTrackWithName(data.StartingTrack).logicTrack,
            data.TransportedCargoPerCar?.ToList()
        );
    }

    public static void Serialize(NetDataWriter writer, TransportTaskData data)
    {
        TaskBeforeDataData.Serialize(writer, data);
        writer.PutArray(data.Cars);
        writer.Put(data.StartingTrack);
        writer.Put(data.DestinationTrack);

        //transport cargo data exists?
        writer.Put(data.TransportedCargoPerCar != null);

        //write data if it exists
        if (data.TransportedCargoPerCar != null)
        {
            writer.PutArray(data.TransportedCargoPerCar?.Select(x => (int)x).ToArray());
            //                   transportedCargoPerCar?.Select(x => (int)x).ToArray()
            Multiplayer.Log("Serialising cargo: " + (int)data.TransportedCargoPerCar[0]);
        }

        writer.Put(data.CouplingRequiredAndNotDone);
        writer.Put(data.AnyHandbrakeRequiredAndNotDone);
    }

    public static TransportTaskData Deserialize(NetDataReader reader)
    {
        Multiplayer.Log("TransportTaskData.Deserialize");
        TransportTaskData data = new TransportTaskData();
        Multiplayer.Log("1");
        Deserialize(reader, data);
        Multiplayer.Log("2");
        data.Cars = reader.GetStringArray();
        Multiplayer.Log("3");
        data.StartingTrack = reader.GetString();
        Multiplayer.Log("4");
        data.DestinationTrack = reader.GetString();
        Multiplayer.Log("5");

        if (reader.GetBool())
        {
            //transport data exists
            data.TransportedCargoPerCar = reader.GetArray<int>(sizeof(int))?.Select(x => (CargoType)x).ToArray();
        }
        
        Multiplayer.Log("TransportedCargoPerCar: " + data.TransportedCargoPerCar?.Select(x => (int)x).ToArray().Join() + "\r\n\t" + data.TransportedCargoPerCar?.ToArray().Join());
        Multiplayer.Log("6");
        data.CouplingRequiredAndNotDone = reader.GetBool();
        Multiplayer.Log("7");
        data.AnyHandbrakeRequiredAndNotDone = reader.GetBool();
        //Multiplayer.Log(JsonConvert.SerializeObject(data, Formatting.Indented));

        return data;
    }
}
