using DV;
using HarmonyLib;
using Multiplayer.Components.Networking;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CarVisitChecker))]
public static class CarVisitCheckerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CarVisitChecker.IsRecentlyVisited), MethodType.Getter)]
    private static bool IsRecentlyVisited_Prefix(ref bool __result)
    {
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;
        __result = true;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CarVisitChecker.RecentlyVisitedRemainingTime), MethodType.Getter)]
    private static bool RecentlyVisitedRemainingTime_Prefix(ref float __result)
    {
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;
        __result = CarVisitChecker.RECENTLY_VISITED_TIME_THRESHOLD;
        return false;
    }
}
