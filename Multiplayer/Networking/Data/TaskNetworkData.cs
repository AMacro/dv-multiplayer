using System;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using LiteNetLib.Utils;
using Multiplayer.Components.Networking.Train;

namespace Multiplayer.Networking.Data;

#region TaskData Base Class
public abstract class TaskNetworkData
{
    public TaskState State { get; set; }
    public float TaskStartTime { get; set; }
    public float TaskFinishTime { get; set; }
    public bool IsLastTask { get; set; }
    public float TimeLimit { get; set; }
    public TaskType TaskType { get; set; }

    public abstract void Serialize(NetDataWriter writer);
    public abstract void Deserialize(NetDataReader reader);
    public abstract Task ToTask();
}
public abstract class TaskNetworkData<T> : TaskNetworkData where T : TaskNetworkData<T>
{
    public abstract T FromTask(Task task);

    protected void SerializeCommon(NetDataWriter writer)
    {
        //Multiplayer.Log($"TaskNetworkData.SerializeCommon() State {(byte)State}, {State}");
        writer.Put((byte)State);
        //Multiplayer.Log($"TaskNetworkData.SerializeCommon() TaskStartTime {TaskStartTime}");
        writer.Put(TaskStartTime);
        //Multiplayer.Log($"TaskNetworkData.SerializeCommon() TaskFinishTime {TaskFinishTime}");
        writer.Put(TaskFinishTime);
        //Multiplayer.Log($"TaskNetworkData.SerializeCommon() IsLastTask {IsLastTask}");
        writer.Put(IsLastTask);
        //Multiplayer.Log($"TaskNetworkData.SerializeCommon() TimeLimit {TimeLimit}");
        writer.Put(TimeLimit);
        //Multiplayer.Log($"TaskNetworkData.SerializeCommon() TaskType {(byte)TaskType}, {TaskType}");
        writer.Put((byte)TaskType);
    }

    protected void DeserializeCommon(NetDataReader reader)
    {
        State = (TaskState)reader.GetByte();
        //Multiplayer.Log($"TaskNetworkData.DeserializeCommon() State {State}");
        TaskStartTime = reader.GetFloat();
        //Multiplayer.Log($"TaskNetworkData.DeserializeCommon() TaskStartTime {TaskStartTime}");
        TaskFinishTime = reader.GetFloat();
        //Multiplayer.Log($"TaskNetworkData.DeserializeCommon() TaskFinishTime {TaskFinishTime}");
        IsLastTask = reader.GetBool();
        //Multiplayer.Log($"TaskNetworkData.DeserializeCommon() IsLastTask {IsLastTask}");
        TimeLimit = reader.GetFloat();
        //Multiplayer.Log($"TaskNetworkData.DeserializeCommon() TimeLimit {TimeLimit}");
        TaskType = (TaskType)reader.GetByte();
        //Multiplayer.Log($"TaskNetworkData.DeserializeCommon() TaskType {TaskType}");
    }
}

#endregion

#region Extension of TaskTypes
public static class TaskNetworkDataFactory
{
    private static readonly Dictionary<Type, Func<Task, TaskNetworkData>> TypeToTaskNetworkData = [];
    private static readonly Dictionary<TaskType, Func<TaskType, TaskNetworkData>> EnumToEmptyTaskNetworkData = [];

    public static void RegisterTaskType<TGameTask>(TaskType taskType, Func<TGameTask, TaskNetworkData> converter, Func<TaskType, TaskNetworkData> emptyCreator)
        where TGameTask : Task
    {
        TypeToTaskNetworkData[typeof(TGameTask)] = task => converter((TGameTask)task);
        EnumToEmptyTaskNetworkData[taskType] = emptyCreator;
    }

    public static TaskNetworkData ConvertTask(Task task)
    {
        //Multiplayer.LogDebug(()=>$"TaskNetworkDataFactory.ConvertTask: Processing task of type {task.GetType()}");
        if (TypeToTaskNetworkData.TryGetValue(task.GetType(), out var converter))
        {
            return converter(task);
        }
        throw new ArgumentException($"Unknown task type: {task.GetType()}");
    }

    public static TaskNetworkData[] ConvertTasks(IEnumerable<Task> tasks)
    {
        return tasks.Select(ConvertTask).ToArray();
    }

    public static TaskNetworkData ConvertTask(TaskType type)
    {
        if (EnumToEmptyTaskNetworkData.TryGetValue(type, out var creator))
        {
            return creator(type);
        }
        throw new ArgumentException($"Unknown task type: {type}");
    }

    // Register base task types
    static TaskNetworkDataFactory()
    {
        RegisterTaskType<WarehouseTask>(
            TaskType.Warehouse,
            task => new WarehouseTaskData { TaskType = TaskType.Warehouse }.FromTask(task),
            type => new WarehouseTaskData { TaskType = type }
        );
        RegisterTaskType<TransportTask>(
            TaskType.Transport,
            task => new TransportTaskData { TaskType = TaskType.Transport }.FromTask(task),
            type => new TransportTaskData { TaskType = type }
        );
        RegisterTaskType<SequentialTasks>(
            TaskType.Sequential,
            task => new SequentialTasksData { TaskType = TaskType.Sequential }.FromTask(task),
            type => new SequentialTasksData { TaskType = type }
        );
        RegisterTaskType<ParallelTasks>(
            TaskType.Parallel,
            task => new ParallelTasksData { TaskType = TaskType.Parallel }.FromTask(task),
            type => new ParallelTasksData { TaskType = type }
        );
    }
}
#endregion

public class WarehouseTaskData : TaskNetworkData<WarehouseTaskData>
{
    public ushort[] CarNetIDs { get; set; }
    public WarehouseTaskType WarehouseTaskType { get; set; }
    public string WarehouseMachine { get; set; }
    public CargoType CargoType { get; set; }
    public float CargoAmount { get; set; }
    public bool ReadyForMachine { get; set; }

    public override void Serialize(NetDataWriter writer)
    {
        SerializeCommon(writer);
        writer.PutArray(CarNetIDs);
        writer.Put((byte)WarehouseTaskType);
        writer.Put(WarehouseMachine);
        writer.Put((int)CargoType);
        writer.Put(CargoAmount);
        writer.Put(ReadyForMachine);
    }

    public override void Deserialize(NetDataReader reader)
    {
        DeserializeCommon(reader);
        CarNetIDs = reader.GetUShortArray();
        WarehouseTaskType = (WarehouseTaskType)reader.GetByte();
        WarehouseMachine = reader.GetString();
        CargoType = (CargoType)reader.GetInt();
        CargoAmount = reader.GetFloat();
        ReadyForMachine = reader.GetBool();
    }

    public override WarehouseTaskData FromTask(Task task)
    {
        if (task is not WarehouseTask warehouseTask)
            throw new ArgumentException("Task is not a WarehouseTask");

        CarNetIDs = warehouseTask.cars
            .Select(car => NetworkedTrainCar.GetFromTrainId(car.ID, out var networkedTrainCar)
                ? networkedTrainCar.NetId
                : (ushort)0)
            .ToArray();
        WarehouseTaskType = warehouseTask.warehouseTaskType;
        WarehouseMachine = warehouseTask.warehouseMachine.ID;
        CargoType = warehouseTask.cargoType;
        CargoAmount = warehouseTask.cargoAmount;
        ReadyForMachine = warehouseTask.readyForMachine;

        return this;
    }

    public override Task ToTask()
    {

        List<Car> cars = CarNetIDs
            .Select(netId => NetworkedTrainCar.GetTrainCar(netId, out TrainCar trainCar) ? trainCar : null)
            .Where(car => car != null)
            .Select(car =>car.logicCar)
            .ToList();

         WarehouseTask newWareTask = new WarehouseTask(
            cars,
            WarehouseTaskType,
            JobSaveManager.Instance.GetWarehouseMachineWithId(WarehouseMachine),
            CargoType,
            CargoAmount
        );

        newWareTask.readyForMachine = ReadyForMachine;

        return newWareTask;
    }
}

public class TransportTaskData : TaskNetworkData<TransportTaskData>
{
    public ushort[] CarNetIDs { get; set; }
    public string StartingTrack { get; set; }
    public string DestinationTrack { get; set; }
    public CargoType[] TransportedCargoPerCar { get; set; }
    public bool CouplingRequiredAndNotDone { get; set; }
    public bool AnyHandbrakeRequiredAndNotDone { get; set; }

    public override void Serialize(NetDataWriter writer)
    {
        SerializeCommon(writer);
        //Multiplayer.LogDebug(() => $"TransportTaskData.Serialize() CarNetIDs count: {CarNetIDs.Length}, Values: [{string.Join(", ", CarNetIDs?.Select(id => id.ToString()))}]");
        writer.PutArray(CarNetIDs);

        //Multiplayer.LogDebug(() => $"TransportTaskData.Serialize() raw after: [{string.Join(", ", writer.Data?.Select(id => id.ToString()))}]");

        //Multiplayer.Log($"TaskNetworkData.Serialize() StartingTrack {StartingTrack}");
        writer.Put(StartingTrack);
        //Multiplayer.Log($"TaskNetworkData.Serialize() DestinationTrack {DestinationTrack}");
        writer.Put(DestinationTrack);

        //Multiplayer.Log($"TaskNetworkData.Serialize() TransportedCargoPerCar != null {TransportedCargoPerCar != null}");
        writer.Put(TransportedCargoPerCar != null);

        if (TransportedCargoPerCar != null)
        {
            //Multiplayer.Log($"TaskNetworkData.Serialize() TransportedCargoPerCar.PutArray() length: {TransportedCargoPerCar.Length}");
            writer.PutArray(TransportedCargoPerCar.Select(x => (int)x).ToArray());
        }

        //Multiplayer.Log($"TaskNetworkData.Serialize() CouplingRequiredAndNotDone {CouplingRequiredAndNotDone}");
        writer.Put(CouplingRequiredAndNotDone);
        //Multiplayer.Log($"TaskNetworkData.Serialize() AnyHandbrakeRequiredAndNotDone {AnyHandbrakeRequiredAndNotDone}");
        writer.Put(AnyHandbrakeRequiredAndNotDone);
    }

    public override void Deserialize(NetDataReader reader)
    {
        DeserializeCommon(reader);

        CarNetIDs = reader.GetUShortArray();

        //Multiplayer.LogDebug(() => $"TransportTaskData.Deserialize() CarNetIDs count: {CarNetIDs.Length}, Values: [{string.Join(", ", CarNetIDs?.Select(id => id.ToString()))}]");

        StartingTrack = reader.GetString();
        //Multiplayer.Log($"TaskNetworkData.Deserialize() StartingTrack {StartingTrack}");
        DestinationTrack = reader.GetString();
        //Multiplayer.Log($"TaskNetworkData.Deserialize() DestinationTrack {DestinationTrack}");

        if (reader.GetBool())
        {
            //Multiplayer.Log($"TaskNetworkData.Deserialize() TransportedCargoPerCar != null True");
            TransportedCargoPerCar = reader.GetIntArray().Select(x => (CargoType)x).ToArray();
        }
        //else
        //{
        //    Multiplayer.LogWarning($"TaskNetworkData.Deserialize() TransportedCargoPerCar != null False");
        //}
        CouplingRequiredAndNotDone = reader.GetBool();
        //Multiplayer.Log($"TaskNetworkData.Deserialize() CouplingRequiredAndNotDone {CouplingRequiredAndNotDone}");
        AnyHandbrakeRequiredAndNotDone = reader.GetBool();
        //Multiplayer.Log($"TaskNetworkData.Deserialize() AnyHandbrakeRequiredAndNotDone {AnyHandbrakeRequiredAndNotDone}");
    }

    public override TransportTaskData FromTask(Task task)
    {
        if (task is not TransportTask transportTask)
            throw new ArgumentException("Task is not a TransportTask");

        //Multiplayer.LogDebug(() => $"TransportTaskData.FromTask() CarNetIDs count: {transportTask.cars.Count()}, Values: [{string.Join(", ", transportTask.cars.Select(car => car.ID))}]");
        CarNetIDs = transportTask.cars
            .Select(car => NetworkedTrainCar.GetFromTrainId(car.ID, out var networkedTrainCar)
                ? networkedTrainCar.NetId
                : (ushort)0)
            .ToArray();     

        //Multiplayer.LogDebug(() => $"TransportTaskData.FromTask() after CarNetIDs count: {CarNetIDs.Length}, Values: [{string.Join(", ", CarNetIDs.Select(id => id.ToString()))}]");

        StartingTrack = transportTask.startingTrack.ID.RailTrackGameObjectID;
        DestinationTrack = transportTask.destinationTrack.ID.RailTrackGameObjectID;
        TransportedCargoPerCar = transportTask.transportedCargoPerCar?.ToArray();
        CouplingRequiredAndNotDone = transportTask.couplingRequiredAndNotDone;
        AnyHandbrakeRequiredAndNotDone = transportTask.anyHandbrakeRequiredAndNotDone;

        return this;
    }

    public override Task ToTask()
    {
        //Multiplayer.LogDebug(() => $"TransportTaskData.ToTask() CarNetIDs !null {CarNetIDs != null}, count: {CarNetIDs?.Length}");

        List<Car> cars = CarNetIDs
            .Select(netId => NetworkedTrainCar.GetTrainCar(netId, out TrainCar trainCar) ? trainCar.logicCar : null)
            .Where(car => car != null)
            .ToList();

        return new TransportTask(
            cars,
            RailTrackRegistry.Instance.GetTrackWithName(DestinationTrack).logicTrack,
            RailTrackRegistry.Instance.GetTrackWithName(StartingTrack).logicTrack,
            TransportedCargoPerCar?.ToList()
        );
    }
}

public class SequentialTasksData : TaskNetworkData<SequentialTasksData>
{
    public TaskNetworkData[] Tasks { get; set; }
    public byte CurrentTaskIndex { get; set; }

    public override void Serialize(NetDataWriter writer)
    {
        //Multiplayer.Log($"SequentialTasksData.Serialize({writer != null})");

        SerializeCommon(writer);

        //Multiplayer.Log($"SequentialTasksData.Serialize() {Tasks.Length}");

        writer.Put((byte)Tasks.Length);
        foreach (var task in Tasks)
        {
            //Multiplayer.Log($"SequentialTasksData.Serialize() {task.TaskType} {task.GetType()}");
            writer.Put((byte)task.TaskType);
            task.Serialize(writer);
        }

        writer.Put(CurrentTaskIndex);
    }

    public override void Deserialize(NetDataReader reader)
    {
        DeserializeCommon(reader);
        var tasksLength = reader.GetByte();
        Tasks = new TaskNetworkData[tasksLength];
        for (int i = 0; i < tasksLength; i++)
        {
            var taskType = (TaskType)reader.GetByte();
            Tasks[i] = TaskNetworkDataFactory.ConvertTask(taskType);
            Tasks[i].Deserialize(reader);
        }

        CurrentTaskIndex = reader.GetByte();

    }

    public override SequentialTasksData FromTask(Task task)
    {
        if (task is not SequentialTasks sequentialTasks)
            throw new ArgumentException("Task is not a SequentialTasks");

        //Multiplayer.Log($"SequentialTasksData.FromTask() {sequentialTasks.tasks.Count}");

        Tasks = TaskNetworkDataFactory.ConvertTasks(sequentialTasks.tasks);

        bool found=false;

        CurrentTaskIndex = 0;
        foreach(Task subTask in sequentialTasks.tasks)
        {
            if(subTask == sequentialTasks.currentTask.Value)
            {
                found = true;
                break;
            }
            CurrentTaskIndex++;
        }

        if (!found)
            CurrentTaskIndex = byte.MaxValue;

        return this;
    }

    public override Task ToTask()
    {
        List<Task> tasks = [];

        foreach (var task in Tasks)
        {
            //Multiplayer.LogDebug(() => $"SequentialTask.ToTask() task not null: {task != null}");

            tasks.Add(task.ToTask());
        }

        SequentialTasks newSeqTask = new SequentialTasks(Tasks.Select(t => t.ToTask()).ToList());

        if(CurrentTaskIndex <= newSeqTask.tasks.Count())
            newSeqTask.currentTask = new LinkedListNode<Task>(newSeqTask.tasks.ToArray()[CurrentTaskIndex]);
        
        return newSeqTask;
    }
}

public class ParallelTasksData : TaskNetworkData<ParallelTasksData>
{
    public TaskNetworkData[] Tasks { get; set; }

    public override void Serialize(NetDataWriter writer)
    {
        SerializeCommon(writer);
        writer.Put((byte)Tasks.Length);
        foreach (var task in Tasks)
        {
            writer.Put((byte)task.TaskType);
            task.Serialize(writer);
        }
    }

    public override void Deserialize(NetDataReader reader)
    {
        DeserializeCommon(reader);
        var tasksLength = reader.GetByte();
        Tasks = new TaskNetworkData[tasksLength];
        for (int i = 0; i < tasksLength; i++)
        {
            var taskType = (TaskType)reader.GetByte();
            Tasks[i] = TaskNetworkDataFactory.ConvertTask(taskType);
            Tasks[i].Deserialize(reader);
        }
    }

    public override ParallelTasksData FromTask(Task task)
    {
        if (task is not ParallelTasks parallelTasks)
            throw new ArgumentException("Task is not a ParallelTasks");

        Tasks = TaskNetworkDataFactory.ConvertTasks(parallelTasks.tasks);

        return this;
    }

    public override Task ToTask()
    {
        return new ParallelTasks(Tasks.Select(t => t.ToTask()).ToList());
    }
}
