using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;
using System.Linq;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(StationProceduralJobsController), nameof(StationProceduralJobsController.TryToGenerateJobs))]
public static class StationProceduralJobsController_TryToGenerateJobs_Patch
{
    private static bool Prefix(StationProceduralJobsController __instance)
    {
        if (NetworkedStationController.GetFromStationController(__instance.stationController, out var networkedStationController))
        {
            if (NetworkLifecycle.Instance.IsHost())
            {
                if (!networkedStationController.StationController.ProceduralJobsController.IsJobGenerationActive)
                {
                    var jobsToSend = __instance.jobChainControllers.Select(jcc => jcc.currentJobInChain).ToList();
                    jobsToSend.RemoveAll(j => networkedStationController.NetworkedJobs.Any(nj => nj.Job.ID == j.ID));
                    if (jobsToSend.Any()) jobsToSend.ForEach(j => networkedStationController.AddJob(j));
                }
            }
            else
            {
                bool generate = !networkedStationController.NetworkedJobs.Any();
                if (generate)
                    Multiplayer.LogDebug(() => $"StationProceduralJobsController_TryToGenerateJobs_Patch: Station {__instance.stationController.stationInfo.YardID} requesting job generation on server");

                networkedStationController.RequestAdditionalJobs(generate);
            }
        }
        return NetworkLifecycle.Instance.IsHost();
    }
}
