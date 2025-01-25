using DV;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;

namespace Multiplayer.Patches.World;

[HarmonyPatch(typeof(CarVisitChecker))]
public static class CarVisitCheckerPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CarVisitChecker.IsRecentlyVisited), MethodType.Getter)]
    private static bool IsRecentlyVisited_Prefix(CarVisitChecker __instance, ref bool __result)
    {
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;    //playing in "vanilla mode" allow game code to run

        if (!NetworkLifecycle.Instance.IsHost())
        {
            //if not the host, we want to keep the car from despawning
            __instance.playerIsInCar = true;
            __result = true; //Pretend there's a player in the car
            return false;   //don't run our vanilla game code
        }
        if (NetworkLifecycle.Instance.Server.ServerPlayers.Count == 0)
        {

            //no server players (this should only apply to a dedicated server), don't despawn
            __instance.playerIsInCar = true;
            __result = true;
            return false;
        }

        //We are the host, check all players against this car
        foreach (ServerPlayer player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (NetworkedTrainCar.TryGetFromTrainCar(__instance.car, out NetworkedTrainCar netTC))
            {
                if (player.CarId == netTC.NetId)
                {
                    __instance.playerIsInCar = true;
                    __result = true;
                    return false;
                }
            }
            else
            {
                //Car was not found, allow it to despawn
                __instance.playerIsInCar = false;
                __result = false;
                return false;
            }
        }

        //No one on the car
        __instance.playerIsInCar = false;
        __result = __instance.recentlyVisitedTimer.RemainingTime > 0f;
        return false;
    }

    /*
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CarVisitChecker.RecentlyVisitedRemainingTime), MethodType.Getter)]
    private static bool RecentlyVisitedRemainingTime_Prefix(ref float __result)
    {
        if (NetworkLifecycle.Instance.IsHost() && NetworkLifecycle.Instance.Server.PlayerCount == 1)
            return true;
        __result = CarVisitChecker.RECENTLY_VISITED_TIME_THRESHOLD;
        return false;
    }
    */
}
