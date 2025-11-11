using DV.Logic.Job;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using System;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(Task))]
public static class TaskPatch
{
    [HarmonyPatch(nameof(Task.SetState))]
    [HarmonyPrefix]
    public static void SetStatePrefix(Task __instance, TaskState newState)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        if (newState == TaskState.InProgress)
            return;


        Multiplayer.LogDebug(()=>$"Task.SetState() called for jobId: {__instance.Job.ID}, taskType: {__instance.InstanceTaskType}, newState: {newState}");

        if (!NetworkedTask.TryGetNetId(__instance, out var taskNetId) ||taskNetId == 0)
        {
            Multiplayer.LogError($"Task.SetState() could not find task index for jobId: {__instance.Job.ID}, taskType: {__instance.InstanceTaskType}");
            return;
        }

        NetworkLifecycle.Instance.Server.SendTaskUpdate(taskNetId, newState, __instance.taskStartTime, __instance.taskFinishTime);
    }

    [HarmonyPatch(nameof(Task.SetJobBelonging))]
    [HarmonyPostfix]
    public static void SetJobBelongingPostfix(Task __instance, Job Job)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return;

        NetworkedJob.EnqueueTask(__instance, Job);
    }
}
