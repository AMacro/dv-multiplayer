using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
using UnityEngine;

namespace Multiplayer.Patches.Jobs;

[HarmonyPatch(typeof(StationJobGenerationRange), nameof(StationJobGenerationRange.PlayerSqrDistanceFromStationCenter), MethodType.Getter)]
public static class StationJobGenerationRange_PlayerSqrDistanceFromStationCenter_Patch
{
    private static bool Prefix(StationJobGenerationRange __instance, ref float __result)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return true;

        Vector3 anchor = __instance.stationCenterAnchor.position;

        __result = float.MaxValue;

        //Loop through all of the players and return the one thats closest to the anchor
        foreach (ServerPlayer serverPlayer in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            float sqDist = (serverPlayer.WorldPosition - anchor).sqrMagnitude;
            if (sqDist < __result)
                __result = sqDist;
        }

        return false;
    }
}

[HarmonyPatch(typeof(StationJobGenerationRange), nameof(StationJobGenerationRange.PlayerSqrDistanceFromStationOffice), MethodType.Getter)]
public static class StationJobGenerationRange_PlayerSqrDistanceFromStationOffice_Patch
{
    private static bool Prefix(StationJobGenerationRange __instance, ref float __result)
    {
        if (!NetworkLifecycle.Instance.IsHost())
            return true;

        Vector3 anchor = __instance.transform.position;

        __result = float.MaxValue;
        //Loop through all of the players and return the one thats closest to the anchor
        foreach (ServerPlayer serverPlayer in NetworkLifecycle.Instance.Server.ServerPlayers)
        {
            float sqDist = (serverPlayer.WorldPosition - anchor).sqrMagnitude;
            if (sqDist < __result)
                __result = sqDist;
        }

        return false;
    }
}
