using DV.Logic.Job;
using System.Collections.Generic;
using System.IO;

namespace MPAPI.Types;

#region TaskData Base Class
/// <summary>
/// Base class for serialising and deserialising job task data for transmission by Multiplayer mod.
/// Not intended for direct use; inherit via <see cref="TaskNetworkData{T}"/>.
/// </summary>
public abstract class TaskNetworkData
{
    /// <summary>
    /// Gets or sets the unique network identifier for this task within its job.
    /// </summary>
    public ushort TaskNetId { get; set; }

    /// <summary>
    /// Gets or sets the current state of the task.
    /// See <see cref="TaskState"/> for possible values.
    /// </summary>
    public TaskState State { get; set; }

    /// <summary>
    /// Gets or sets the time at which the task started, in seconds since the job began.
    /// </summary>
    public float TaskStartTime { get; set; }

    /// <summary>
    /// Gets or sets the time at which the task finished, in seconds since the job began.
    /// </summary>
    public float TaskFinishTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the last task in the job sequence.
    /// </summary>
    public bool IsLastTask { get; set; }

    /// <summary>
    /// Gets or sets the time limit for completing the task, in seconds.
    /// </summary>
    public float TimeLimit { get; set; }

    /// <summary>
    /// Gets or sets the type of the task.
    /// See <see cref="DV.Logic.Job.TaskType"/> for possible values.
    /// </summary>
    public TaskType TaskType { get; set; }

    /// <summary>
    /// Serialises the task network data to the specified <see cref="BinaryWriter"/>.
    /// Implementations should write all relevant fields for network transmission.
    /// </summary>
    /// <remarks>
    /// The first line of the implementation should call <see cref="TaskNetworkData{T}.SerializeCommon(BinaryWriter)"/>.
    /// </remarks>
    /// <param name="writer">The <see cref="BinaryWriter"/> to write data to.</param>
    public abstract void Serialize(BinaryWriter writer);

    /// <summary>
    /// Deserialises the task network data from the specified <see cref="BinaryReader"/>.
    /// Implementations should read all relevant fields in the same order and size as written by <see cref="Serialize(BinaryWriter)"/>.
    /// </summary>
    /// <remarks>
    /// The first line of the implementation should call <see cref="TaskNetworkData{T}.DeserializeCommon(BinaryReader)"/>.
    /// </remarks>
    /// <param name="reader">The <see cref="BinaryReader"/> to read data from.</param>
    public abstract void Deserialize(BinaryReader reader);

    /// <summary>
    /// Converts this <see cref="TaskNetworkData"/> instance into a <see cref="Task"/> object
    /// compatible with the job/task system, and adds them to the provided dictionary.
    /// </summary>
    /// <param name="netIdToTask">
    /// A reference to a <see cref="Dictionary{TKey, TValue}"/> that will be populated with deserialized <see cref="Task"/> instances.
    /// Each key is a netTaskId (<c>ushort</c>), and each value is the corresponding <see cref="Task"/> object.
    /// </param>
    /// <returns>A <see cref="Task"/> instance representing the deserialized data.</returns>
    /// <remarks>
    /// Implementations should add all relevant <see cref="Task"/> instances to <paramref name="netIdToTask"/>.
    /// This allows aggregation of multiple tasks from different <see cref="TaskNetworkData"/> objects into a single dictionary.
    /// </remarks>
    public abstract Task ToTask(ref Dictionary<ushort, Task> netIdToTask);

    /// <summary>
    /// Gets a list of car IDs (<see cref="ushort"/>) associated with this task.
    /// </summary>
    /// <returns>A list of car IDs relevant to the task.</returns>
    public abstract List<ushort> GetCars();
}

/// <summary>
/// Generic abstract base class providing type-safe conversion for serialising and deserialising job task data.
/// Inherit from this class to implement serialisers for custom <see cref="Task"/> types.
/// </summary>
/// <typeparam name="T">The concrete type that inherits from this class.</typeparam>
public abstract class TaskNetworkData<T> : TaskNetworkData where T : TaskNetworkData<T>
{
    /// <summary>
    /// Populates this <see cref="TaskNetworkData"/> instance from the specified <see cref="Task"/> object.
    /// </summary>
    /// <param name="task">The <see cref="Task"/> to extract data from.</param>
    /// <remarks>This method is called by Multiplayer mod when serialising a job.</remarks>
    /// <returns>This instance, populated with data from the provided task.</returns>
    public abstract T FromTask(Task task);

    /// <summary>
    /// Extracts and populates the common task data fields from the specified <see cref="Task"/> object.
    /// Should be called as the first step in the <c>FromTask</c> implementation of derived classes.
    /// </summary>
    /// <param name="task">The <see cref="Task"/> to extract data from.</param>
    protected void FromTaskCommon(Task task)
    {
        State = task.state;
        TaskStartTime = task.taskStartTime;
        TaskFinishTime = task.taskFinishTime;
        IsLastTask = task.IsLastTask;
        TimeLimit = task.TimeLimit;
    }

    /// <summary>
    /// Populates common task data fields to the specified <see cref="Task"/> object.
    /// Should be called after the new task has been instantiated <c>ToTask</c> implementation of derived classes.
    /// </summary>
    /// <param name="task">The <see cref="Task"/> to populate.</param>
    protected void ToTaskCommon(Task task)
    {
        task.state = State;
        task.taskStartTime = TaskStartTime;
        task.taskFinishTime = TaskFinishTime;
        task.isLastTask = IsLastTask;
        task.TimeLimit = TimeLimit;
    }

    /// <summary>
    /// Serialises the common task data fields to the specified <see cref="BinaryWriter"/>.
    /// Should be called as the first step in the <c>Serialize</c> implementation of derived classes.
    /// </summary>
    /// <param name="writer">The <see cref="BinaryWriter"/> to write data to.</param>
    protected void SerializeCommon(BinaryWriter writer)
    {
        writer.Write(TaskNetId);
        writer.Write((byte)State);
        writer.Write(TaskStartTime);
        writer.Write(TaskFinishTime);
        writer.Write(IsLastTask);
        writer.Write(TimeLimit);
        writer.Write((byte)TaskType);
    }


    /// <summary>
    /// Deserialises the common task data fields from the specified <see cref="BinaryReader"/>.
    /// Should be called as the first step in the <c>Deserialize</c> implementation of derived classes.
    /// </summary>
    /// <param name="reader">The <see cref="BinaryReader"/> to read data from.</param>
    protected void DeserializeCommon(BinaryReader reader)
    {
        TaskNetId = reader.ReadUInt16();
        State = (TaskState)reader.ReadByte();
        TaskStartTime = reader.ReadSingle();
        TaskFinishTime = reader.ReadSingle();
        IsLastTask = reader.ReadBoolean();
        TimeLimit = reader.ReadSingle();
        TaskType = (TaskType)reader.ReadByte();
    }
}

#endregion
