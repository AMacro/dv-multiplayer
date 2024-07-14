using DV.Logic.Job;
using HarmonyLib;
using Multiplayer.Components;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Utils;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(Station), nameof(Station.AddJobToStation))]
public static class Station_AddJobToStation_Patch
{
    private static bool Prefix(Station __instance, Job job)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return false;

        Multiplayer.Log($"Station_AddJobToStation_Patch adding NetworkJob for stationId: {__instance.ID}, jobId: {job.ID}");
        
        StationController stationController;
        if(!StationComponentLookup.Instance.StationControllerFromId(__instance.ID, out stationController))
            return false;
        
        NetworkedJob netJob = stationController.gameObject.AddComponent<NetworkedJob>();
        if (netJob != null)
        {
            netJob.job=job;
            netJob.stationID = __instance.ID;

        }
        return true;
    }
}
