using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(StationController))]
public static class StationController_Patch
{
    [HarmonyPatch(nameof(StationController.Awake))]
    [HarmonyPostfix]
    public static void Awake(StationController __instance)
    {
        __instance.gameObject.AddComponent<NetworkedStationController>();
    }

    [HarmonyPatch(nameof(StationController.ExpireAllAvailableJobsInStation))]
    [HarmonyPrefix]
    public static bool ExpireAllAvailableJobsInStation(StationController __instance)
    {
        return NetworkLifecycle.Instance.IsHost();
    }


}
