using HarmonyLib;
using Multiplayer.Components.Networking;
using Multiplayer.Utils;
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

        __result = anchor.AnyPlayerSqrMag();

        //Multiplayer.Log($"PlayerSqrDistanceFromStationCenter() {__result}");

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

        __result = anchor.AnyPlayerSqrMag();

        return false;
    }
}
