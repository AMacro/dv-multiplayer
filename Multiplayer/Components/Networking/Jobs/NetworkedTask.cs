using DV.Logic.Job;
using System;
using System.Collections.Generic;

namespace Multiplayer.Components.Networking.Jobs;

public class NetworkedTask : IdMonoBehaviour<ushort, NetworkedTask>
{
    #region Lookup Cache
    private static readonly Dictionary<Task, NetworkedTask> taskToNetworkedTask = [];

    public static bool TryGet(ushort netId, out NetworkedTask networkedTask)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedTask> rawObj);
        networkedTask = (NetworkedTask)rawObj;
        return b;
    }

    public static bool TryGet(ushort netId, out Task task)
    {
        task = null;

        if (!Get(netId, out IdMonoBehaviour<ushort, NetworkedTask> rawObj) || rawObj == null)
            return false;

        task = ((NetworkedTask)rawObj).Task;

        return task != null;
    }

    public static bool TryGetNetId(Task task, out ushort netId)
    {
        if (taskToNetworkedTask.TryGetValue(task, out var networkedTask) && networkedTask != null)
        {
            netId = networkedTask.NetId;
            return true;
        }

        netId = 0;
        return false;
    }
    #endregion

    protected override bool IsIdServerAuthoritative => true;

    public Task Task { get; private set; }

    public void Initialize(Task task, ushort netId = 0)
    {
        if (task == null)
        {
            Multiplayer.LogError($"NetworkedTask.Initialize(): Task is null\r\n{Environment.StackTrace}");
            return;
        }

        if (taskToNetworkedTask.ContainsKey(task))
        {
            Multiplayer.LogError($"NetworkedTask.Initialize(): Task {task.InstanceTaskType} for jobId {task.Job.ID} is already registered");
            Destroy(this);
            return;
        }

        Task = task;
        taskToNetworkedTask[Task] = this;
        if (netId != 0)
            NetId = netId;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (Task != null)
            taskToNetworkedTask.Remove(Task);
    }
}
