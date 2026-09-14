using DV;
using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using System;

namespace Multiplayer.Patches.Train;

[HarmonyPatch(typeof(CarVisitChecker))]
public static class CarVisitCheckerPatch
{
    [ThreadStatic]
    private static int physicsLodTransitionDepth;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TrainPhysicsLod), "SetLod")]
    private static void BeginPhysicsLodTransition() => physicsLodTransitionDepth++;

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(TrainPhysicsLod), "SetLod")]
    private static Exception EndPhysicsLodTransition(Exception __exception)
    {
        physicsLodTransitionDepth--;
        return __exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.UnloadInterior))]
    private static bool KeepOccupiedNetworkCarInteriorLoaded(TrainCar __instance)
    {
        if (physicsLodTransitionDepth == 0 ||
            !NetworkLifecycle.Instance.IsHost() ||
            !NetworkedTrainCar.TryGetFromTrainCar(__instance, out NetworkedTrainCar networkedCar))
            return true;

        return !networkedCar.HasPlayers() && !networkedCar.IsInteriorPinnedForNetworkSync;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CarVisitChecker.IsRecentlyVisited), MethodType.Getter)]
    public static bool IsRecentlyVisited_Prefix(CarVisitChecker __instance, ref bool __result)
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

        if (!NetworkedTrainCar.TryGetFromTrainCar(__instance.car, out NetworkedTrainCar netTC))
        {
            //Car was not found, allow it to despawn
            __instance.playerIsInCar = false;
            __result = false;
            return false;
        }

        //We are the host, check all players against this car
        foreach (ServerPlayer player in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            if (player.CarId == netTC.NetId)
            {
                __instance.playerIsInCar = true;
                __result = true;
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

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CarVisitChecker.OnPlayerCarChanged))]
    private static bool OnPlayerCarChanged_Prefix(CarVisitChecker __instance)
    {
        if (!NetworkedTrainCar.TryGetFromTrainCar(__instance.car, out NetworkedTrainCar netTC) || netTC == null)
            return true;

        if (netTC.HasPlayers())
        {
            __instance.playerIsInCar = true;
            __instance.recentlyVisitedTimer.StopCountdown();
        }
        else if (__instance.playerIsInCar)
        {
            // if there was a player in the car, but now there isn't, start the countdown
            // without this check, the countdown will be reset every time a player enters or leaves any car.

            __instance.playerIsInCar = false;
            __instance.recentlyVisitedTimer.StartCountdown(CarVisitChecker.RECENTLY_VISITED_TIME_THRESHOLD, CarVisitChecker.COUNTDOWN_TIME_UNIT);

            if (__instance.propagateToFront)
                __instance.VisitConnectedCars(__instance.car?.frontCoupler?.coupledTo);

            if (__instance.propagateToRear)
                __instance.VisitConnectedCars(__instance.car?.rearCoupler?.coupledTo);
        }
        return false;
    }

}
