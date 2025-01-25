using DV.Logic.Job;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(Station), nameof(Station.AddJobToStation))]
public static class Station_AddJobToStation_Patch
{
    private static bool Prefix(Station __instance, Job job)
    {
        Multiplayer.Log($"Station.AddJobToStation() adding NetworkJob for stationId: {__instance.ID}, jobId: {job.ID}");

        if (NetworkLifecycle.Instance.IsHost())
        {
            if(!NetworkedStationController.GetFromStationId(__instance.ID, out NetworkedStationController netStationController))
                return false;
        
            netStationController.AddJob(job);
        }

        return true;
    }
}
