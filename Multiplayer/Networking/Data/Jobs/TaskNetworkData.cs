using DV.Logic.Job;
using DV.ThingTypes;
using MPAPI.Types;
using MPAPI.Util;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Train;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using DV.ThingTypes.TransitionHelpers;

namespace Multiplayer.Networking.Data.Jobs;

#region Extension of TaskTypes
public static class TaskNetworkDataFactory
{
    private static readonly Dictionary<Type, Func<Task, TaskNetworkData>> TypeToTaskNetworkData = [];
    private static readonly Dictionary<TaskType, Func<TaskType, TaskNetworkData>> EnumToEmptyTaskNetworkData = [];
    internal static readonly List<Type> baseTasks = [];
    internal static readonly List<TaskType> baseTaskTypes = [];

    public static bool RegisterTaskType<TGameTask, TNetworkData>(TaskType taskType)
        where TGameTask : Task
        where TNetworkData : TaskNetworkData<TNetworkData>, new()
    {
        Multiplayer.LogDebug(() => $"Registering Task Type {typeof(TGameTask)} with TaskType {taskType}");

        if (TypeToTaskNetworkData.Keys.Contains(typeof(TGameTask)) || EnumToEmptyTaskNetworkData.Keys.Contains(taskType))
        {
            Multiplayer.LogError($"Task Type {typeof(TGameTask)} already registered!");
            return false;
        }

        TypeToTaskNetworkData[typeof(TGameTask)] = task =>
        {
            var networkData = new TNetworkData { TaskType = taskType };
            return ((TaskNetworkData<TNetworkData>)networkData).FromTask(task);
        };

        EnumToEmptyTaskNetworkData[taskType] = type => new TNetworkData { TaskType = type };

        return true;
    }

    public static bool UnregisterTaskType<TGameTask>(TaskType taskType)
        where TGameTask : Task
    {
        Multiplayer.LogDebug(() => $"Unregistering Task Type {typeof(TGameTask)} with TaskType {taskType}");
        if (baseTasks.Contains(typeof(TGameTask)) || baseTaskTypes.Contains(taskType))
        {
            Multiplayer.LogError($"Cannot unregister base task type {typeof(TGameTask)} with TaskType {taskType}");
            return false;
        }

        TypeToTaskNetworkData.Remove(typeof(TGameTask));
        EnumToEmptyTaskNetworkData.Remove(taskType);

        return true;
    }

    public static TaskNetworkData ConvertTask(Task task)
    {
        Multiplayer.LogDebug(() => $"TaskNetworkDataFactory.ConvertTask: Processing task of type {task.InstanceTaskType}");
        if (TypeToTaskNetworkData.TryGetValue(task.GetType(), out var converter))
        {
            var taskData = converter(task);

            if (NetworkedTask.TryGetNetId(task, out var taskNetId) && taskNetId != 0)
                taskData.TaskNetId = taskNetId;
            else
                Multiplayer.LogError($"TaskNetworkDataFactory.ConvertTask: Could not find NetworkedJob for jobId: {task.Job.ID}, taskType: {task.InstanceTaskType}");

            return taskData;
        }
        throw new ArgumentException($"Unknown task type: {task.GetType()}");
    }

    public static TaskNetworkData[] ConvertTasks(IEnumerable<Task> tasks)
    {
        return tasks.Select(ConvertTask).ToArray();
    }

    public static TaskNetworkData ConvertTask(TaskType taskType)
    {
        //Multiplayer.LogDebug(() => $"TaskNetworkDataFactory.ConvertTask({type})");
        if (EnumToEmptyTaskNetworkData.TryGetValue(taskType, out var creator))
        {
            return creator(taskType);
        }
        throw new ArgumentException($"Unknown task type: {taskType}");
    }

    // Register base task types
    static TaskNetworkDataFactory()
    {
        RegisterTaskType<WarehouseTask, WarehouseTaskData>(TaskType.Warehouse);

        baseTasks.Add(typeof(WarehouseTask));
        baseTaskTypes.Add(TaskType.Warehouse);

        RegisterTaskType<TransportTask, TransportTaskData>(TaskType.Transport);

        baseTasks.Add(typeof(TransportTask));
        baseTaskTypes.Add(TaskType.Transport);

        RegisterTaskType<SequentialTasks, SequentialTasksData>(TaskType.Sequential);

        baseTasks.Add(typeof(SequentialTasks));
        baseTaskTypes.Add(TaskType.Sequential);

        RegisterTaskType<ParallelTasks, ParallelTasksData>(TaskType.Parallel);

        baseTasks.Add(typeof(ParallelTasks));
        baseTaskTypes.Add(TaskType.Parallel);
    }
}
#endregion

#region Base Task Types

public class WarehouseTaskData : TaskNetworkData<WarehouseTaskData>
{
    public ushort[] CarNetIDs { get; set; }
    public WarehouseTaskType WarehouseTaskType { get; set; }
    public string WarehouseMachine { get; set; }
    public CargoType CargoType { get; set; }
    public float CargoAmount { get; set; }
    public bool ReadyForMachine { get; set; }

    public override void Serialize(BinaryWriter writer)
    {
        SerializeCommon(writer);
        writer.WriteUShortArray(CarNetIDs);
        writer.Write((byte)WarehouseTaskType);
        writer.Write(WarehouseMachine ?? string.Empty);

        if (!CargoTypeLookup.Instance.TryGetNetId(CargoType.ToV2(), out var cargoNetId))
            Multiplayer.LogError($"WarehouseTaskData.Serialize(): Could not find netId for CargoType {CargoType}");

        writer.Write(cargoNetId);
        writer.Write(CargoAmount);
        writer.Write(ReadyForMachine);
    }

    public override void Deserialize(BinaryReader reader)
    {
        DeserializeCommon(reader);
        CarNetIDs = reader.ReadUShortArray();
        WarehouseTaskType = (WarehouseTaskType)reader.ReadByte();
        WarehouseMachine = reader.ReadString();

        uint cargoNetId = reader.ReadUInt32();
        CargoTypeLookup.Instance.TryGet(cargoNetId, out CargoType cargoType);
        CargoType = cargoType;

        CargoAmount = reader.ReadSingle();
        ReadyForMachine = reader.ReadBoolean();
    }

    public override WarehouseTaskData FromTask(Task task)
    {
        if (task is not WarehouseTask warehouseTask)
            throw new ArgumentException("Task is not a WarehouseTask");

        FromTaskCommon(task);

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

    public override Task ToTask(ref Dictionary<ushort, Task> netIdToTask)
    {
        List<Car> cars = CarNetIDs
            .Select(netId => NetworkedTrainCar.TryGet(netId, out TrainCar trainCar) ? trainCar : null)
            .Where(car => car != null)
            .Select(car => car.logicCar)
            .ToList();

        WarehouseTask newWarehouseTask = new
        (
           cars,
           WarehouseTaskType,
           JobSaveManager.Instance.GetWarehouseMachineWithId(WarehouseMachine),
           CargoType,
           CargoAmount,
           (long)TimeLimit,
           IsLastTask
        );

        ToTaskCommon(newWarehouseTask);

        newWarehouseTask.readyForMachine = ReadyForMachine;

        netIdToTask.Add(TaskNetId, newWarehouseTask);

        return newWarehouseTask;
    }

    public override List<ushort> GetCars()
    {
        return CarNetIDs.ToList();
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

    public override void Serialize(BinaryWriter writer)
    {
        SerializeCommon(writer);
        //Multiplayer.LogDebug(() => $"TransportTaskData.Serialize() CarNetIDs count: {CarNetIDs.Length}, Values: [{string.Join(", ", CarNetIDs?.Select(id => id.ToString()))}]");
        writer.WriteUShortArray(CarNetIDs);

        //Multiplayer.LogDebug(() => $"TransportTaskData.Serialize() raw after: [{string.Join(", ", writer.Data?.Select(id => id.ToString()))}]");

        //Multiplayer.Log($"TaskNetworkData.Serialize() StartingTrack {StartingTrack}");
        writer.Write(StartingTrack);
        //Multiplayer.Log($"TaskNetworkData.Serialize() DestinationTrack {DestinationTrack}");
        writer.Write(DestinationTrack);

        //Multiplayer.Log($"TaskNetworkData.Serialize() TransportedCargoPerCar != null {TransportedCargoPerCar != null}");

        writer.Write(TransportedCargoPerCar?.Length ?? 0);

        if (TransportedCargoPerCar != null)
        {
            foreach (var cargoType in TransportedCargoPerCar)
            {
                CargoTypeLookup.Instance.TryGetNetId(cargoType.ToV2(), out var cargoNetId);
                writer.Write(cargoNetId);
            }
        }

        //Multiplayer.Log($"TaskNetworkData.Serialize() CouplingRequiredAndNotDone {CouplingRequiredAndNotDone}");
        writer.Write(CouplingRequiredAndNotDone);
        //Multiplayer.Log($"TaskNetworkData.Serialize() AnyHandbrakeRequiredAndNotDone {AnyHandbrakeRequiredAndNotDone}");
        writer.Write(AnyHandbrakeRequiredAndNotDone);
    }

    public override void Deserialize(BinaryReader reader)
    {
        DeserializeCommon(reader);

        CarNetIDs = reader.ReadUShortArray();

        //Multiplayer.LogDebug(() => $"TransportTaskData.Deserialize() CarNetIDs count: {CarNetIDs.Length}, Values: [{string.Join(", ", CarNetIDs?.Select(id => id.ToString()))}]");

        StartingTrack = reader.ReadString();
        //Multiplayer.Log($"TaskNetworkData.Deserialize() StartingTrack {StartingTrack}");
        DestinationTrack = reader.ReadString();
        //Multiplayer.Log($"TaskNetworkData.Deserialize() DestinationTrack {DestinationTrack}");

        var cargoCount = reader.ReadInt32();
        if (cargoCount > 0)
        {
            TransportedCargoPerCar = new CargoType[cargoCount];

            for (var i = 0; i < cargoCount; i++)
            {
                uint cargoNetId = reader.ReadUInt32();
                CargoTypeLookup.Instance.TryGet(cargoNetId, out CargoType cargoType);
                TransportedCargoPerCar[i] = cargoType;
            }
        }

        CouplingRequiredAndNotDone = reader.ReadBoolean();
        //Multiplayer.Log($"TaskNetworkData.Deserialize() CouplingRequiredAndNotDone {CouplingRequiredAndNotDone}");
        AnyHandbrakeRequiredAndNotDone = reader.ReadBoolean();
        //Multiplayer.Log($"TaskNetworkData.Deserialize() AnyHandbrakeRequiredAndNotDone {AnyHandbrakeRequiredAndNotDone}");
    }

    public override TransportTaskData FromTask(Task task)
    {
        if (task is not TransportTask transportTask)
            throw new ArgumentException("Task is not a TransportTask");

        FromTaskCommon(task);

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

    public override Task ToTask(ref Dictionary<ushort, Task> netIdToTask)
    {
        List<Car> cars = CarNetIDs
            .Select(netId => NetworkedTrainCar.TryGet(netId, out TrainCar trainCar) ? trainCar.logicCar : null)
            .Where(car => car != null)
            .ToList();

        var newTransportTask = new TransportTask
        (
            cars,
            RailTrackRegistry.Instance.GetTrackWithName(DestinationTrack).LogicTrack(),
            RailTrackRegistry.Instance.GetTrackWithName(StartingTrack).LogicTrack(),
            TransportedCargoPerCar?.ToList(),
            (long)TimeLimit,
            IsLastTask
        );

        ToTaskCommon(newTransportTask);

        netIdToTask.Add(TaskNetId, newTransportTask);

        return newTransportTask;
    }

    public override List<ushort> GetCars()
    {
        return CarNetIDs.ToList();
    }
}

public class SequentialTasksData : TaskNetworkData<SequentialTasksData>
{
    public TaskNetworkData[] Tasks { get; set; }


    public override void Serialize(BinaryWriter writer)
    {
        //Multiplayer.Log($"SequentialTasksData.Serialize({writer != null})");

        SerializeCommon(writer);

        //Multiplayer.Log($"SequentialTasksData.Serialize() {Tasks.Length}");

        writer.Write((byte)Tasks.Length);
        foreach (var task in Tasks)
        {
            //Multiplayer.Log($"SequentialTasksData.Serialize() {task.TaskType} {task.GetType()}");
            writer.Write((byte)task.TaskType);
            task.Serialize(writer);
        }
    }

    public override void Deserialize(BinaryReader reader)
    {
        DeserializeCommon(reader);
        var tasksLength = reader.ReadByte();
        Tasks = new TaskNetworkData[tasksLength];
        for (int i = 0; i < tasksLength; i++)
        {
            var taskType = (TaskType)reader.ReadByte();
            Tasks[i] = TaskNetworkDataFactory.ConvertTask(taskType);
            Tasks[i].Deserialize(reader);
        }
    }

    public override SequentialTasksData FromTask(Task task)
    {
        if (task is not SequentialTasks sequentialTasks)
            throw new ArgumentException("Task is not a SequentialTasks");

        FromTaskCommon(task);

        Tasks = TaskNetworkDataFactory.ConvertTasks(sequentialTasks.tasks);

        return this;
    }

    public override Task ToTask(ref Dictionary<ushort, Task> netIdToTask)
    {
        List<Task> tasks = [];

        foreach (var task in Tasks)
        {
            var taskResults = task.ToTask(ref netIdToTask);
            tasks.Add(taskResults);
        }

        SequentialTasks newSequentialTask = new(tasks, (long)TimeLimit);

        ToTaskCommon(newSequentialTask);

        netIdToTask.Add(TaskNetId, newSequentialTask);

        // Rebuild linked list task states - this is the equivalent of OverrideTasksStates(TaskSaveData[] tasksData)
        int index = 0;
        for (var currentNode = newSequentialTask.tasks.First; currentNode != null; currentNode = currentNode.Next)
        {
            currentNode.Value.state = tasks[index].state;
            currentNode.Value.taskStartTime = tasks[index].taskStartTime;
            currentNode.Value.taskFinishTime = tasks[index].taskFinishTime;

            if (tasks[index].state == TaskState.Done && currentNode != newSequentialTask.tasks.Last)
                newSequentialTask.currentTask = currentNode.Next;

            index++;
        }

        return newSequentialTask;
    }

    public override List<ushort> GetCars()
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

public class ParallelTasksData : TaskNetworkData<ParallelTasksData>
{
    public TaskNetworkData[] Tasks { get; set; }

    public override void Serialize(BinaryWriter writer)
    {
        SerializeCommon(writer);
        writer.Write((byte)Tasks.Length);
        foreach (var task in Tasks)
        {
            writer.Write((byte)task.TaskType);
            task.Serialize(writer);
        }
    }

    public override void Deserialize(BinaryReader reader)
    {
        DeserializeCommon(reader);
        var tasksLength = reader.ReadByte();
        Tasks = new TaskNetworkData[tasksLength];
        for (int i = 0; i < tasksLength; i++)
        {
            var taskType = (TaskType)reader.ReadByte();
            Tasks[i] = TaskNetworkDataFactory.ConvertTask(taskType);
            Tasks[i].Deserialize(reader);
        }
    }

    public override ParallelTasksData FromTask(Task task)
    {
        if (task is not ParallelTasks parallelTasks)
            throw new ArgumentException("Task is not a ParallelTasks");

        FromTaskCommon(task);

        Tasks = TaskNetworkDataFactory.ConvertTasks(parallelTasks.tasks);

        return this;
    }

    public override Task ToTask(ref Dictionary<ushort, Task> netIdToTask)
    {
        List<Task> taskList = new(Tasks.Length);

        for (int i = 0; i < Tasks.Length; i++)
            taskList.Add(Tasks[i].ToTask(ref netIdToTask));

        var newParallelTasks = new ParallelTasks(taskList, (long)TimeLimit, IsLastTask);

        ToTaskCommon(newParallelTasks);

        netIdToTask.Add(TaskNetId, newParallelTasks);

        return newParallelTasks;
    }

    public override List<ushort> GetCars()
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
#endregion
